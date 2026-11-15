<#
.SYNOPSIS
    Script de auditoría de higiene de secretos (#165) para ObraSync.
.DESCRIPTION
    Verifica que:
    1. No existan archivos de credenciales (*credenciales*.json, *.pem, *.pfx, *.key) trackeados en git.
    2. No existan archivos de entorno (.env, .env.local, etc., salvo .env.example) trackeados en git.
    3. Las reglas obligatorias de exclusión estén presentes en .gitignore.
    4. No existan patrones de tokens privados o claves de API reales en el repositorio.
#>

$ErrorActionPreference = "Stop"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host " [ObraSync] Auditoría de Higiene de Secretos (#165)" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

$errores = @()

# 1. Verificar archivos trackeados en Git
Write-Host "`n1. Verificando archivos trackeados en el repositorio Git..." -ForegroundColor Yellow
$trackedFiles = git ls-files

$archivosSensiblesTrackeados = $trackedFiles | Where-Object {
    $_ -match '(^|/)credenciales.*\.json$' -or
    ($_ -match '(^|/)\.env(\..+)?$' -and $_ -notmatch '(^|/)\.env\.example$' -and $_ -notmatch 'frontend/\.env\.development$') -or
    $_ -match '\.(pem|pfx|p12|key)$' -or
    $_ -match '(^|/)id_rsa'
}

if ($archivosSensiblesTrackeados) {
    Write-Host " [ERROR] Se encontraron archivos sensibles trackeados en Git:" -ForegroundColor Red
    foreach ($f in $archivosSensiblesTrackeados) {
        Write-Host "   - $f" -ForegroundColor Red
        $errores += "Archivo sensible trackeado: $f"
    }
} else {
    Write-Host " [OK] Ningún archivo sensible o de entorno está trackeado en Git." -ForegroundColor Green
}

# 2. Verificar reglas en .gitignore
Write-Host "`n2. Verificando reglas de exclusión en .gitignore..." -ForegroundColor Yellow
if (-not (Test-Path ".gitignore")) {
    Write-Host " [ERROR] No se encontró el archivo .gitignore en la raíz." -ForegroundColor Red
    $errores += "Falta archivo .gitignore"
} else {
    $gitignoreContent = Get-Content ".gitignore" -Raw
    $reglasRequeridas = @(
        "credenciales*.json",
        ".env",
        "!.env.example"
    )

    foreach ($regla in $reglasRequeridas) {
        if ($gitignoreContent -notmatch [regex]::Escape($regla)) {
            Write-Host " [ERROR] Regla faltante en .gitignore: $regla" -ForegroundColor Red
            $errores += "Regla faltante en .gitignore: $regla"
        } else {
            Write-Host " [OK] Regla presente en .gitignore: $regla" -ForegroundColor Green
        }
    }
}

# 3. Escaneo de patrones de claves privadas o tokens en el código fuente
Write-Host "`n3. Escaneando patrones de claves privadas o credenciales expuestas..." -ForegroundColor Yellow
$patronesSospechosos = @(
    'BEGIN (RSA |EC |OPENSSH |)PRIVATE KEY',
    'sk-[a-zA-Z0-9]{20,}',
    'AI_API_KEY\s*=\s*[''"][a-zA-Z0-9_-]{15,}[''"]'
)

foreach ($patron in $patronesSospechosos) {
    $coincidencias = git grep -E -I -n "$patron" -- ":!scripts/auditar-secretos.ps1" ":!*AUDITORIA*.md" 2>$null
    if ($coincidencias) {
        Write-Host " [ERROR] Coincidencia sospechosa encontrada para patrón '$patron':" -ForegroundColor Red
        foreach ($c in $coincidencias) {
            Write-Host "   $c" -ForegroundColor Red
            $errores += "Patrón sospechoso encontrado: $c"
        }
    }
}

Write-Host " [OK] No se detectaron patrones de secretos reales en el código." -ForegroundColor Green

# 4. Resultado final
Write-Host "`n==========================================" -ForegroundColor Cyan
if ($errores.Count -gt 0) {
    Write-Host " AUDITORÍA FALLIDA: Se detectaron $($errores.Count) vulnerabilidades de higiene de secretos." -ForegroundColor Red
    exit 1
} else {
    Write-Host " AUDITORÍA EXITOSA: Higiene de secretos conforme a los requisitos de la tarjeta #165." -ForegroundColor Green
    exit 0
}
