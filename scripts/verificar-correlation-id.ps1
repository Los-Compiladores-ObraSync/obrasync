# verificar-correlation-id.ps1 - tarjeta #104: un solo X-Correlation-ID de punta a punta
# Manda UN id propio por el Gateway en todo el recorrido Requisicion -> Licitacion -> OC -> Recepcion y despues
# comprueba (1) que cada respuesta devuelve ese mismo id y (2) que aparece en los logs de cada servicio,
# incluidos los consumidores de RabbitMQ (la prueba de que el id viajo dentro de los eventos).
# Uso (con docker compose levantado, desde cualquier carpeta): .\scripts\verificar-correlation-id.ps1
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$gw = 'http://localhost:5000'
# Tiene que ser un GUID: los eventos llevan el CorrelationId como Guid.
$id = [guid]::NewGuid().ToString()
Write-Host "X-Correlation-ID de esta corrida: $id" -ForegroundColor Cyan

$encabezados = New-Object System.Collections.Generic.List[object]

function Api($method, $path, $token, $body) {
  $h = @{ 'X-Correlation-ID' = $id }
  if ($token) { $h['Authorization'] = "Bearer $token" }
  $p = @{ Method = $method; Uri = "$gw$path"; Headers = $h; UseBasicParsing = $true; ContentType = 'application/json; charset=utf-8' }
  if ($body) { $p.Body = [System.Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 6)) }
  try { $r = Invoke-WebRequest @p }
  catch {
    Write-Host "ERROR en $method $path : $($_.Exception.Message)" -ForegroundColor Red
    if ($_.ErrorDetails -and $_.ErrorDetails.Message) { Write-Host $_.ErrorDetails.Message }
    throw
  }
  $devuelto = $r.Headers['X-Correlation-ID']
  if ($devuelto -is [array]) { $devuelto = $devuelto[0] }
  $encabezados.Add([pscustomobject]@{ Pedido = "$method $path"; Ok = ($devuelto -eq $id); Devuelto = $devuelto })
  if ($r.Content) {
    $obj = $r.Content | ConvertFrom-Json
    foreach ($e in $obj) { $e }   # desarma las listas, como hace Invoke-RestMethod
  }
}

function Login($email) {
  $r = Api Post '/api/usuarios/tokens' $null @{ email = $email; password = 'Password123!' }
  @{ Id = $r.id; Token = $r.token }
}

$capataz   = Login 'capataz@obrasync.com'
$comprador = Login 'comprador@obrasync.com'
$proveedor = Login 'proveedor@obrasync.com'
Write-Host '1. Logins OK'

$obra = (Api Get '/api/Obras' $comprador.Token) | Where-Object { $_.etapas.Count -gt 0 } | Select-Object -First 1
$insumos = @((Api Get '/api/catalogo/insumos' $capataz.Token) | Where-Object { $_.activo } | Select-Object -First 2)
Write-Host "2. Obra: $($obra.nombre) / insumos: $($insumos.codigo -join ', ')"

$cantidad = [math]::Round(10 + (Get-Random -Minimum 1 -Maximum 99) / 100, 2)
$req = Api Post '/api/requisiciones' $capataz.Token @{
  obraId = $obra.id; etapa = $obra.etapas[0].nombreEtapa; observaciones = 'Verificacion #104'
  items  = @($insumos | ForEach-Object { @{ articuloId = $_.articuloId; cantidad = $cantidad } })
}
Write-Host "3. Requisicion creada: $($req.numeroRequisicion)"

$null = Api Post "/api/requisiciones/$($req.requisicionId)/aprobaciones" $comprador.Token
Write-Host '4. Requisicion aprobada (publica RequisicionAprobadaEvent)'
Start-Sleep -Seconds 4

$lic = (Api Get '/api/licitaciones' $comprador.Token) | Where-Object { $_.requisicionId -eq $req.requisicionId } | Select-Object -First 1
if (-not $lic) {
  $lic = Api Post '/api/licitaciones' $comprador.Token @{
    requisicionId = $req.requisicionId
    fechaLimiteCotizacion = (Get-Date).ToUniversalTime().AddDays(2).ToString('o')
  }
}
Write-Host "5. Licitacion: $($lic.licitacionId)"

$cot = Api Post '/api/cotizaciones' $proveedor.Token @{
  licitacionId = $lic.licitacionId; proveedorId = $proveedor.Id; flete = 1500.75; plazoEntregaDias = 5
  items = @($insumos | ForEach-Object { @{ articuloId = $_.articuloId; precioUnitario = 1234.56 } })
}
$null = Api Post "/api/licitaciones/$($lic.licitacionId)/adjudicaciones" $comprador.Token @{ cotizacionId = $cot.cotizacionId }
Write-Host '6. Adjudicada (publica OrdenCompraEmitidaEvent)'
Start-Sleep -Seconds 4

$null = Api Get '/api/recepciones/ordenes-pendientes' $capataz.Token
Write-Host '7. Ordenes pendientes consultadas'

# --- Resultados ---
Write-Host ''
Write-Host 'Encabezado de respuesta (cada pedido devuelve el mismo id):' -ForegroundColor Cyan
$malos = @($encabezados | Where-Object { -not $_.Ok })
Write-Host ("  {0} pedidos, {1} con id distinto o ausente" -f $encabezados.Count, $malos.Count)
$malos | ForEach-Object { Write-Host "  FALLA: $($_.Pedido) devolvio '$($_.Devuelto)'" -ForegroundColor Red }

# Cada servicio: contenedor y el texto que prueba que fue ESE servicio quien registro el id.
# Licitaciones y Recepcion se prueban por el log de su CONSUMIDOR: ahi el id solo puede llegar dentro del evento.
$chequeos = @(
  @{ Nombre = 'Usuarios (HTTP)';                      Servicio = 'usuarios-obras-api';          Patron = 'Procesando solicitud' },
  @{ Nombre = 'Requisiciones (HTTP)';                 Servicio = 'requisiciones-catalogo-api';  Patron = 'Procesando solicitud' },
  @{ Nombre = 'Licitaciones (consume RequisicionAprobadaEvent)'; Servicio = 'licitaciones-cotizaciones-api'; Patron = 'RequisicionAprobada recibida' },
  @{ Nombre = 'Recepcion (consume OrdenCompraEmitidaEvent)';     Servicio = 'recepcion-presupuesto-api';     Patron = 'Presupuesto comprometido|SinPartidaPresupuestaria|bloqueada por desv|duplicado|OrdenCompraEmitidaEvent' }
)
Write-Host ''
Write-Host 'Logs de cada servicio (docker compose logs):' -ForegroundColor Cyan
$fallas = $malos.Count
foreach ($c in $chequeos) {
  $logs = (docker compose logs --no-color --since 15m $c.Servicio 2>&1 | Out-String) -split "`n"
  $lineas = @($logs | Where-Object { $_.Contains($id) -and $_ -match $c.Patron })
  if ($lineas.Count -gt 0) { Write-Host ("  OK     {0}: {1} linea(s)" -f $c.Nombre, $lineas.Count) -ForegroundColor Green }
  else { Write-Host ("  FALTA  {0}: el id no aparece en los logs de {1}" -f $c.Nombre, $c.Servicio) -ForegroundColor Red; $fallas++ }
}

Write-Host ''
if ($fallas -eq 0) { Write-Host "TODO OK: el id $id viajo de la PWA/Gateway hasta los consumidores." -ForegroundColor Green; exit 0 }
Write-Host "HAY $fallas FALLA(S). Revisa a mano con: docker compose logs <servicio> | Select-String $id" -ForegroundColor Red
exit 1
