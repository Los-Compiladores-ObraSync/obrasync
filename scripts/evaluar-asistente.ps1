<#
.SYNOPSIS
  Corre la evaluación del asistente (tarjeta #145) contra el modelo REAL y deja el informe en asistente/evaluacion/resultados.
.DESCRIPTION
  Gasta cupo compartido del grupo (#144): avisá en el canal antes. Corré de a un rol por vez.
  La clave se toma de AI_API_KEY o se pide sin mostrarla; nunca se escribe en un archivo.
.EXAMPLE
  .\scripts\evaluar-asistente.ps1 -Rol Capataz
#>
param(
    [ValidateSet('Capataz', 'Comprador', 'Admin')]
    [Parameter(Mandatory = $true)]
    [string]$Rol,

    # Segundos de pausa entre casos, para no pasarse del cupo por minuto.
    [int]$PausaSegundos = 15
)

$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot

$respuesta = Read-Host "¿Ya avisaste en el canal que vas a usar el modelo real? (s/n)"
if ($respuesta -ne 's') { Write-Host 'Avisá primero en el canal y volvé a correrlo.'; exit 1 }

if (-not $env:AI_BASE_URL) { $env:AI_BASE_URL = Read-Host 'AI_BASE_URL (la dirección del gateway de IA)' }
if (-not $env:AI_DEPLOYMENT) {
    $d = Read-Host 'AI_DEPLOYMENT (Enter = shared-unaj-newsku-resource/gpt-5-mini)'
    $env:AI_DEPLOYMENT = if ($d) { $d } else { 'shared-unaj-newsku-resource/gpt-5-mini' }
}
if (-not $env:AI_API_KEY) {
    $segura = Read-Host 'AI_API_KEY (no se muestra)' -AsSecureString
    $env:AI_API_KEY = [System.Net.NetworkCredential]::new('', $segura).Password
}

$env:EVAL_REAL = '1'
$env:EVAL_ROL = $Rol
$env:EVAL_PAUSA_SEG = "$PausaSegundos"

try {
    dotnet test "$raiz\asistente\ObraSync.Asistente.Tests" --filter "Category=EvaluacionReal" --logger "console;verbosity=detailed"
}
finally {
    # Que la clave no quede en el entorno de esta sesión.
    Remove-Item Env:AI_API_KEY, Env:EVAL_REAL, Env:EVAL_ROL, Env:EVAL_PAUSA_SEG -ErrorAction SilentlyContinue
}
