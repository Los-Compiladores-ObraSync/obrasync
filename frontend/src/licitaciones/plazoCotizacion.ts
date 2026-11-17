// Plazo para cotizar de una licitación. Lo comparten la pantalla de la requisición y el asistente de Compras,
// así los dos publican con la MISMA regla: el plazo vence a las 23:59 (hora local de la persona) del día elegido,
// y la API lo recibe en UTC.

/** "YYYY-MM-DD" en hora local, a tantos días de hoy (es el formato del <input type="date">). */
export function fechaLocalEnDias(dias: number): string {
  const f = new Date()
  f.setDate(f.getDate() + dias)
  const mes = String(f.getMonth() + 1).padStart(2, '0')
  const dia = String(f.getDate()).padStart(2, '0')
  return `${f.getFullYear()}-${mes}-${dia}`
}

/** El plazo vence al final del día elegido (hora local); la API lo recibe en UTC. */
export function finDelDiaEnUtc(fechaLocal: string): string {
  return new Date(`${fechaLocal}T23:59:00`).toISOString()
}
