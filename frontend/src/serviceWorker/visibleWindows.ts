interface VentanaVisiblePush {
  visibilityState: string
  postMessage(message: unknown, transfer: Transferable[]): void
}

/** Ante cualquier fallo de inspección o comunicación, el respaldo es mostrar la notificación. */
export async function hayVentanaVisible(
  consultarVentanas: () => Promise<readonly VentanaVisiblePush[]>,
): Promise<boolean> {
  let ventanas: readonly VentanaVisiblePush[]
  try {
    ventanas = await consultarVentanas()
  } catch {
    return false
  }

  let visibles: VentanaVisiblePush[]
  try {
    visibles = ventanas.filter((ventana) => ventana.visibilityState === 'visible')
  } catch {
    return false
  }
  if (visibles.length === 0) return false

  const respuestas = visibles.map((ventana) => new Promise<boolean>((resolve) => {
    let canal: MessageChannel | undefined
    let temporizador: ReturnType<typeof setTimeout> | undefined
    try {
      canal = new MessageChannel()
      temporizador = setTimeout(() => {
        canal?.port1.close()
        resolve(false)
      }, 700)
      canal.port1.onmessage = (evento) => {
        if (temporizador !== undefined) clearTimeout(temporizador)
        canal?.port1.close()
        resolve(evento.data === 'visible')
      }
      ventana.postMessage({ type: 'OBRASYNC_PUSH_VISIBILITY_QUERY' }, [canal.port2])
    } catch {
      if (temporizador !== undefined) clearTimeout(temporizador)
      canal?.port1.close()
      resolve(false)
    }
  }))

  try {
    const estados = await Promise.all(respuestas)
    return estados.some(Boolean)
  } catch {
    return false
  }
}
