/** "Juan Pérez" → "JP". Sirve para el círculo con las iniciales. */
export function iniciales(nombre: string): string {
  return nombre
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((palabra) => palabra[0].toUpperCase())
    .join('')
}
