# adjudicar-rabbit-caido-e2e.ps1 - #135: adjudicar con RabbitMQ apagado (outbox)
$gw = 'http://localhost:5000'

function Login($email) {
  $r = Invoke-RestMethod -Method Post -Uri "$gw/api/usuarios/login" -ContentType 'application/json' `
       -Body (@{ email = $email; password = 'Password123!' } | ConvertTo-Json)
  @{ Id = $r.id; Headers = @{ Authorization = "Bearer $($r.token)" } }
}
function Api($method, $path, $user, $body) {
  $p = @{ Method = $method; Uri = "$gw$path"; Headers = $user.Headers; ContentType = 'application/json; charset=utf-8' }
  if ($body) { $p.Body = [System.Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 6)) }
  Invoke-RestMethod @p
}
function Cuerpo($err) {
  if ($err.ErrorDetails -and $err.ErrorDetails.Message) { return $err.ErrorDetails.Message }
  try { $s = $err.Exception.Response.GetResponseStream(); if ($s.CanSeek) { $s.Position = 0 }; return (New-Object System.IO.StreamReader($s)).ReadToEnd() } catch { return '(sin cuerpo)' }
}

$capataz   = Login 'capataz@obrasync.com'
$comprador = Login 'comprador@obrasync.com'
$proveedor = Login 'proveedor@obrasync.com'

$obra = (Api Get '/api/Obras' $comprador) | Where-Object { $_.etapas.Count -gt 0 } | Select-Object -First 1
$insumos = (Api Get '/api/catalogo/insumos' $capataz) | Where-Object { $_.activo } | Select-Object -First 2
$cantidad = [math]::Round(10 + (Get-Random -Minimum 1 -Maximum 99) / 100, 2)

$req = Api Post '/api/requisiciones' $capataz @{
  obraId = $obra.id; etapa = $obra.etapas[0].nombreEtapa; observaciones = 'E2E #135 outbox adjudicar'
  items  = @($insumos | ForEach-Object { @{ articuloId = $_.articuloId; cantidad = $cantidad } })
}
$null = Api Post "/api/requisiciones/$($req.requisicionId)/aprobar" $comprador
Write-Host "1. Requisicion aprobada: $($req.numeroRequisicion)"
Start-Sleep -Seconds 3

$lic = (Api Get '/api/licitaciones' $comprador) | Where-Object { $_.requisicionId -eq $req.requisicionId } | Select-Object -First 1
if (-not $lic) {
  $lic = Api Post '/api/licitaciones' $comprador @{
    requisicionId = $req.requisicionId
    fechaLimiteCotizacion = (Get-Date).ToUniversalTime().AddDays(2).ToString('o')
  }
}
$cot = Api Post '/api/cotizaciones' $proveedor @{
  licitacionId = $lic.licitacionId; proveedorId = $proveedor.Id; flete = 1500.75; plazoEntregaDias = 5
  items = @($insumos | ForEach-Object { @{ articuloId = $_.articuloId; precioUnitario = 1234.56 } })
}
Write-Host "2. Licitacion $($lic.licitacionId) con cotizacion lista."

Write-Host ""
Write-Host ">>> Ahora apagá RabbitMQ en otra terminal: docker compose stop rabbitmq" -ForegroundColor Yellow
Read-Host ">>> Cuando esté apagado, apretá Enter para adjudicar"

$sw = [Diagnostics.Stopwatch]::StartNew()
try {
  $null = Api Post "/api/licitaciones/$($lic.licitacionId)/adjudicar" $comprador @{ cotizacionId = $cot.cotizacionId }
  Write-Host "3. Adjudicar respondio OK en $($sw.Elapsed.TotalSeconds.ToString('0.0')) s"
} catch {
  Write-Host "3. Adjudicar FALLO en $($sw.Elapsed.TotalSeconds.ToString('0.0')) s -> HTTP $([int]$_.Exception.Response.StatusCode)" -ForegroundColor Yellow
  Write-Host "   Respuesta: $(Cuerpo $_)"
}

$actual = Api Get "/api/licitaciones/$($lic.licitacionId)" $comprador
Write-Host "4. Estado final de la licitacion: $($actual.estado)"
Write-Host ""
Write-Host ">>> Ahora prendé RabbitMQ: docker compose start rabbitmq" -ForegroundColor Yellow
Read-Host ">>> Esperá unos segundos, y apretá Enter para ver ordenes pendientes"
$pend = Api Get '/api/recepciones/ordenes-pendientes' $capataz
Write-Host "5. Ordenes pendientes en Recepcion: $($pend.Count)"
$pend | ConvertTo-Json -Depth 5