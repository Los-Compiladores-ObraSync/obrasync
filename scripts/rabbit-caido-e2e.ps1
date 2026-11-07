# rabbit-caido-e2e.ps1 - B.5: aprobar una requisicion con RabbitMQ apagado
# Uso: apagar RabbitMQ (docker compose stop rabbitmq), correr este script, volver a prenderlo.
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
$obra = (Api Get '/api/Obras' $comprador) | Where-Object { $_.etapas.Count -gt 0 } | Select-Object -First 1
$insumos = (Api Get '/api/catalogo/insumos' $capataz) | Where-Object { $_.activo } | Select-Object -First 2
$cantidad = [math]::Round(10 + (Get-Random -Minimum 1 -Maximum 99) / 100, 2)

$req = Api Post '/api/requisiciones' $capataz @{
  obraId = $obra.id; etapa = $obra.etapas[0].nombreEtapa; observaciones = 'E2E B.5 rabbit caido'
  items  = @($insumos | ForEach-Object { @{ articuloId = $_.articuloId; cantidad = $cantidad } })
}
Write-Host "1. Requisicion creada: $($req.numeroRequisicion) ($($req.estado))"

$sw = [Diagnostics.Stopwatch]::StartNew()
try {
  $null = Api Post "/api/requisiciones/$($req.requisicionId)/aprobar" $comprador
  Write-Host "2. Aprobar respondio OK en $($sw.Elapsed.TotalSeconds.ToString('0.0')) s"
} catch {
  Write-Host "2. Aprobar FALLO en $($sw.Elapsed.TotalSeconds.ToString('0.0')) s -> HTTP $([int]$_.Exception.Response.StatusCode)" -ForegroundColor Yellow
  Write-Host "   Respuesta: $(Cuerpo $_)"
}

$actual = Api Get "/api/requisiciones/$($req.requisicionId)" $comprador
Write-Host "3. Estado final de $($req.numeroRequisicion): $($actual.estado)"
Write-Host "   (requisicionId = $($req.requisicionId))"
