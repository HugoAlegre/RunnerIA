import { RunResponse } from './models';

/** Mensaje legacy del API antes del fix de exit code 1. */
const RX_ERROR_CODIGO_1 = /^El proceso terminó con código\s*1\.?$/i;

export function esCorridaVacia(data: RunResponse): boolean {
  return data.corridaVacia === true;
}

export function esFallaEscenarios(data: RunResponse): boolean {
  return data.fallasEscenarios === true || data.exitCode === 1;
}

export function esErrorProceso(data: RunResponse): boolean {
  if (data.errorProceso === true) return true;
  const c = data.exitCode;
  return typeof c === 'number' && c !== 0 && c !== 1;
}

export function esCorridaDetenida(data: RunResponse): boolean {
  return /detenida por el operador/i.test(String(data.error || data.log || ''));
}

/** Texto para UI / Excel: no mostrar «código 1» como error de proceso. */
export function mensajeCorrida(data: RunResponse): string {
  if (esCorridaDetenida(data)) return 'Corrida detenida por el operador.';

  if (esCorridaVacia(data)) {
    const err = String(data.error || '').trim();
    return err || 'No se ejecutó ningún escenario — revisar filtro/tag.';
  }

  const err = String(data.error || '').trim();
  if (esFallaEscenarios(data)) {
    if (err && !RX_ERROR_CODIGO_1.test(err)) return err;
    return 'Escenarios con fallas (ver informe).';
  }

  if (esErrorProceso(data)) {
    return err || `Error del proceso (código ${data.exitCode}).`;
  }

  return err;
}

/** Resultado obtenido para Excel cuando hay fallas de escenario. */
export function resultadoObtenidoCorrida(
  data: RunResponse,
  pass: boolean,
  fallback = 'No cumple el resultado esperado.'
): string {
  if (pass) return '';
  const msg = mensajeCorrida(data);
  if (msg && !RX_ERROR_CODIGO_1.test(msg)) return msg;
  const hint = extraerObservacionDeLog(data.log || '');
  return hint || fallback;
}

/** Extrae avisos útiles del log (Assert, Exception, SC-…) sin volcar todo el log. */
export function extraerObservacionDeLog(log: string): string {
  if (!log) return '';
  const lineas = log.split(/\r?\n/).map((l) => l.trim()).filter(Boolean);
  const claves = lineas.filter((l) =>
    /Assert|Exception|FAIL|Error|SC-\d+|Expected|Expected:|but was/i.test(l)
  );
  if (!claves.length) return '';
  const t = claves.slice(-5).join('\n');
  return t.length <= 800 ? t : t.slice(0, 799) + '…';
}
