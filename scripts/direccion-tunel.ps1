# Muestra la dirección HTTPS del túnel para abrir la app en el celular.
# Uso (con el túnel ya levantado: docker compose --profile movil up -d --build):
#   .\scripts\direccion-tunel.ps1
# Espera hasta ~60 segundos a que Cloudflare asigne la dirección.
$patron = 'https://[a-z0-9-]+\.trycloudflare\.com'
for ($i = 0; $i -lt 30; $i++) {
    $logs = docker compose logs tunel 2>&1 | Out-String
    $m = [regex]::Match($logs, $patron)
    if ($m.Success) {
        Write-Host ""
        Write-Host "Abrí esta dirección en el celular:" -ForegroundColor Green
        Write-Host $m.Value -ForegroundColor Cyan
        Write-Host ""
        Write-Host "La dirección cambia cada vez que se reinicia el túnel. Apagalo al terminar: docker compose --profile movil down"
        exit 0
    }
    Start-Sleep -Seconds 2
}
Write-Host "No encontré la dirección. ¿Levantaste con 'docker compose --profile movil up -d --build'? Probá: docker compose logs tunel" -ForegroundColor Yellow
exit 1
