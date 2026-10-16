export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  correlationId?: string
}

export class ApiError extends Error {
  readonly status: number
  readonly title: string
  readonly detail?: string
  readonly correlationId?: string

  constructor(status: number, problem: ProblemDetails = {}) {
    super(problem.detail ?? problem.title ?? `Error ${status}`)
    this.name = 'ApiError'
    this.status = status
    this.title = problem.title ?? 'Error'
    this.detail = problem.detail
    this.correlationId = problem.correlationId
  }
}
