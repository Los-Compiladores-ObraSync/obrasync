# flujo-e2e.ps1 - recorrido B.2: Requisicion -> Licitacion -> OC -> Recepcion, todo por el Gateway
$ErrorActionPreference = 'Stop'
$gw = 'http://localhost:5000'

function Login($email) {
  $r = Invoke-RestMethod -Method Post -Uri "$gw/api/usuarios/login" -ContentType 'application/json' `
       -Body (@{ email = $email; password = 'Password123!' } | ConvertTo-Json)
  @{ Id = $r.id; Headers = @{ Authorization = "Bearer $($r.token)" } }
}
function Api($method, $path, $user, $body) {
  $p = @{ Method = $method; Uri = "$gw$path"; Headers = $user.Headers; ContentType = 'application/json; charset=utf-8' }
  if ($body) { $p.Body = [System.Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 6)) }
  try { Invoke-RestMethod @p }
  catch {
    $cuerpo = ''
    if ($_.ErrorDetails -and $_.ErrorDetails.Message) { $cuerpo = $_.ErrorDetails.Message }
    elseif ($_.Exception.Response) {
      try {
        $stream = $_.Exception.Response.GetResponseStream()
        if ($stream.CanSeek) { $stream.Position = 0 }
        $cuerpo = (New-Object System.IO.StreamReader($stream)).ReadToEnd()
      } catch { $cuerpo = '(no se pudo leer el cuerpo)' }
    }
    Write-Host "ERROR en $method $path" -ForegroundColor Red
    if ($p.Body) { Write-Host "Enviado: $([System.Text.Encoding]::UTF8.GetString($p.Body))" }
    Write-Host "HTTP: $([int]$_.Exception.Response.StatusCode)"
    Write-Host "Respuesta: $cuerpo"
    throw
  }
}

$capataz   = Login 'capataz@obrasync.com'
$comprador = Login 'comprador@obrasync.com'
$proveedor = Login 'proveedor@obrasync.com'
Write-Host "1. Logins OK (proveedorId = $($proveedor.Id))"

$obra = (Api Get '/api/Obras' $comprador) | Where-Object { $_.etapas.Count -gt 0 } | Select-Object -First 1
$insumos = (Api Get '/api/catalogo/insumos' $capataz) | Where-Object { $_.activo } | Select-Object -First 2
Write-Host "2. Obra: $($obra.nombre) / etapa: $($obra.etapas[0].nombreEtapa) / insumos: $($insumos.codigo -join ', ')"

# La cantidad cambia en cada corrida: el servicio rechaza (409) requisiciones identicas recientes
$cantidad = [math]::Round(10 + (Get-Random -Minimum 1 -Maximum 99) / 100, 2)
Write-Host "   cantidad de esta corrida: $cantidad"
$req = Api Post '/api/requisiciones' $capataz @{
  obraId = $obra.id; etapa = $obra.etapas[0].nombreEtapa; observaciones = 'E2E #124'
  items  = @($insumos | ForEach-Object { @{ articuloId = $_.articuloId; cantidad = $cantidad } })
}
Write-Host "3. Requisicion creada: $($req.numeroRequisicion) ($($req.estado))"

$null = Api Post "/api/requisiciones/$($req.requisicionId)/aprobar" $comprador
Write-Host "4. Requisicion aprobada"
Start-Sleep -Seconds 3

$lic = (Api Get '/api/licitaciones' $comprador) | Where-Object { $_.requisicionId -eq $req.requisicionId } | Select-Object -First 1
if (-not $lic) {
  $lic = Api Post '/api/licitaciones' $comprador @{
    requisicionId = $req.requisicionId
    fechaLimiteCotizacion = (Get-Date).ToUniversalTime().AddDays(2).ToString('o')
  }
  Write-Host "5. Licitacion creada por API: $($lic.licitacionId)"
} else { Write-Host "5. Licitacion creada por el evento: $($lic.licitacionId)" }

$cot = Api Post '/api/cotizaciones' $proveedor @{
  licitacionId = $lic.licitacionId; proveedorId = $proveedor.Id; flete = 1500.75; plazoEntregaDias = 5
  items = @($insumos | ForEach-Object { @{ articuloId = $_.articuloId; precioUnitario = 1234.56 } })
}
Write-Host "6. Cotizacion enviada"

$null = Api Post "/api/licitaciones/$($lic.licitacionId)/adjudicar" $comprador @{ cotizacionId = $cot.cotizacionId }
Write-Host "7. Adjudicada -> OC emitida"
Start-Sleep -Seconds 3

$pend = Api Get '/api/recepciones/ordenes-pendientes' $capataz
Write-Host "8. Ordenes pendientes en Recepcion: $($pend.Count)"
$pend | ConvertTo-Json -Depth 5
