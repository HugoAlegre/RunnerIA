export interface Categoria {
  id: string;
  nombre: string;
  sub?: string;
  /** Descripción larga del módulo (qué cubre la regresión del módulo). */
  descripcion?: string;
  escenarios: string[];
  testing?: boolean;
}

export interface Escenario {
  num?: string;
  tipo?: string;
  titulo: string;
  corto?: string;
  descripcion?: string;
  tag?: string;
  resultado?: string;
  script?: string;
  extraArgs?: string;
  prerequisitos?: string[];
  evidencias?: string[];
  /** Si true, «Ejecutar» corre todas las pruebas del módulo y genera Excel/ZIP QA. */
  regresionModulo?: boolean;
  /** Si true, al ejecutar este caso se genera Excel QA (PNG embebidos + queries). Default: true salvo lotes/diagnóstico. */
  entregaQaExcel?: boolean;
  generada?: boolean;
  pruebaArchivo?: string;
  rutaRelativa?: string;
  grupoId?: string;
}

/** Prefijos que el Runner resalta en prerequisitos (criterio / cajas). */
export function esPrerequisitoResaltado(texto: string): boolean {
  const t = (texto || '').trim();
  return (
    t.startsWith('★') ||
    t.startsWith('[CRITICO]') ||
    /^OBLIGATORIO:/i.test(t) ||
    /CRITERIO:/i.test(t) ||
    /CAJA[S]?:/i.test(t)
  );
}

/** Ítem en cola FIFO de ejecución del Runner. */
export interface ColaItem {
  colaId: string;
  escenarioId: string;
  titulo: string;
  encoladoEn: number;
}

export type RunConflictAction = 'enqueue' | 'stop-and-run' | 'cancel';

export interface RunResponse {
  runId: string;
  escenarioId?: string;
  titulo?: string;
  estado?: string;
  ok?: boolean;
  /** true cuando dotnet test terminó con código 1 (fallas de escenario, no error de proceso). */
  fallasEscenarios?: boolean;
  /** Exit 0 pero 0 tests / solo Omitidos. */
  corridaVacia?: boolean;
  errorProceso?: boolean;
  exitCode?: number | null;
  error?: string;
  iniciado?: string;
  finalizado?: string;
  log?: string;
  tieneEvidencia?: boolean;
  evidenciaCarpeta?: string;
  evidenciaInforme?: string;
  evidenciaZipUrl?: string;
  evidenciaZipNombre?: string;
  evidenciaInformeUrl?: string;
  /** ZIP de la última corrida de este escenario (persiste tras reiniciar API). */
  evidenciaZipEscenarioUrl?: string;
  /** Informe HTML de la última corrida de este escenario. */
  evidenciaInformeEscenarioUrl?: string;
}

export interface UsuarioSot {
  rol?: string;
  usuario?: string;
  password?: string;
  tienePassword?: boolean;
}

export interface ConfigCliente {
  ok?: boolean;
  ambiente?: string;
  urlInicio?: string;
  authOpenIdUrl?: string;
  permitirAuthQa?: boolean;
  sucursal?: string;
  codigoSucursal?: string;
  usuario?: string;
  passwordLogin?: string;
  usuariosSot?: UsuarioSot[];
  cobisServidor?: string;
  cobisHost?: string;
  puertoSybase?: string;
  cobisUsuario?: string;
  cobisBaseDatos?: string;
  passwordCobis?: string;
  sqlSotServidor?: string;
  sqlSotPuerto?: string;
  sqlSotUsuario?: string;
  sqlSotBaseDatos?: string;
  passwordSqlSot?: string;
  secretsExiste?: boolean;
  tienePasswordLogin?: boolean;
  tienePasswordCobis?: boolean;
  tienePasswordSqlSot?: boolean;
  /** SC-416 — correlativos editables */
  cierreForzadoNotaCompensada?: string;
  cierreForzadoNotaNormal?: string;
  cierreForzadoNotaMiniBoveda?: string;
  /** Retiro efectivo — consulta COBIS */
  cobisConsultaMaxCuentas?: string;
  cobisConsultaModoRotacion?: string;
  cobisConsultaRotacionPorCliente?: string;
  cobisConsultaUnaCuentaPorCliente?: string;
  cobisConsultaSaldoMinimoPesos?: string;
  cobisConsultaSaldoMinimoExtranjera?: string;
  cobisConsultaSaldoMinimoCategorizada?: string;
  cobisConsultaTitularidadDefault?: string;
  /** Retiro efectivo — importe aleatorio en UI */
  retiroImporteMinimo?: string;
  retiroImporteMaximoPractico?: string;
  retiroMargenSaldoResiduo?: string;
  retiroPorcentajeMinimo?: string;
  retiroPorcentajeMaximo?: string;
  [key: string]: unknown;
}
