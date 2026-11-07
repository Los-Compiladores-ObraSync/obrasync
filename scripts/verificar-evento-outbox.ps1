# verificar-evento-outbox.ps1 - comprueba que el evento RequisicionAprobada llego a Licitaciones
# despues de haber aprobado con RabbitMQ caido (#134). Reintenta hasta 12 veces, cada 10 segundos.
param([Parameter(Mandatory = $true)][string]$RequisicionId)

$gw = 'http://localhost:5000'
$r = Invoke-RestMethod -Method Post -Uri "$gw/api/usuarios/login" -ContentType 'application/json' `
     -Body (@{ email = 'comprador@obrasync.com'; password = 'Password123!' } | ConvertTo-Json)
$h = @{ Authorization = "Bearer $($r.token)" }
$body = @{ requisicionId = $RequisicionId; fechaLimiteCotizacion = (Get-Date).ToUniversalTime().AddDays(2).ToString('o') } | ConvertTo-Json

for ($i = 1; $i -le 12; $i++) {
  try {
    $lic = Invoke-RestMethod -Method Post -Uri "$gw/api/licitaciones" -Headers $h -ContentType 'application/json' -Body $body
    Write-Host "Intento $i : LICITACION CREADA ($($lic.licitacionId))" -ForegroundColor Green
    Write-Host "RESULTADO: OK - el evento llego a Licitaciones despues de que RabbitMQ volvio." -ForegroundColor Green
    return
  } catch {
    $codigo = [int]$_.Exception.Response.StatusCode
    Write-Host "Intento $i : HTTP $codigo - todavia no llego, espero 10 segundos..."
    Start-Sleep -Seconds 10
  }
}
Write-Host "RESULTADO: FALLO - despues de 2 minutos el evento no llego." -ForegroundColor Red
