import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { API_BASE } from '../api-base';
import { ConfigCliente, RunResponse } from '../models';

export interface ModuloQaRequest {
  nombreModulo: string;
  ticket?: string;
  casos: Array<{
    id: string;
    titulo: string;
    tag?: string;
    precondiciones: string;
    pasos: string;
    resultadoEsperado: string;
    resultadoObtenido: string;
    estado: string;
    observaciones?: string;
  }>;
  corridas: Array<{
    casoId: string;
    titulo: string;
    tag?: string;
    escenarioId: string;
    runId?: string;
    evidenciaCarpeta?: string;
  }>;
}

@Injectable({ providedIn: 'root' })
export class ApiService {
  readonly online = signal(false);
  readonly busy = signal(false);
  /** Suite AutomatizacionSOT encontrada y usable para corridas. */
  readonly automatizacionOk = signal(true);
  readonly modoAyuda = signal(false);
  readonly suiteMensaje = signal('');

  constructor(private http: HttpClient) {}

  async health(): Promise<boolean> {
    try {
      const r = await firstValueFrom(
        this.http.get<{
          ok?: boolean;
          automatizacionOk?: boolean;
          modoAyuda?: boolean;
          mensaje?: string | null;
        }>(`${API_BASE}/health`)
      );
      this.online.set(!!r?.ok);
      const suiteOk = r?.automatizacionOk !== false;
      this.automatizacionOk.set(suiteOk);
      this.modoAyuda.set(!!r?.modoAyuda || !suiteOk);
      this.suiteMensaje.set(
        r?.mensaje ||
          (!suiteOk
            ? 'Falta AutomatizacionSOT junto al portable. La UI funciona; las corridas no.'
            : '')
      );
      return !!r?.ok;
    } catch {
      this.online.set(false);
      this.automatizacionOk.set(false);
      this.modoAyuda.set(false);
      this.suiteMensaje.set('');
      return false;
    }
  }

  getPreflight() {
    return this.http.get<{
      ok?: boolean;
      dotnetOk?: boolean;
      secretsPresent?: boolean;
      playwrightPath?: string;
      playwrightOk?: boolean;
      escenariosCount?: number;
      catalogSyncOk?: boolean;
      catalogUiOnly?: string[];
      catalogApiOnly?: string[];
      automatizacionOk?: boolean;
      modoAyuda?: boolean;
      mensaje?: string | null;
    }>(`${API_BASE}/preflight`);
  }

  getConfigCliente() {
    return this.http.get<ConfigCliente>(`${API_BASE}/config/cliente`);
  }

  postConfigCliente(body: ConfigCliente) {
    return this.http.post<{ ok?: boolean; error?: string; mensaje?: string }>(
      `${API_BASE}/config/cliente`,
      body
    );
  }

  probarSqlSot(body: ConfigCliente) {
    return this.http.post<{
      ok?: boolean;
      error?: string;
      mensaje?: string;
      motor?: string;
      baseDatos?: string;
      servidor?: string;
    }>(`${API_BASE}/config/cliente/probar-sql-sot`, body);
  }

  probarCobis(body: ConfigCliente) {
    return this.http.post<{
      ok?: boolean;
      error?: string;
      mensaje?: string;
      motor?: string;
      baseDatos?: string;
      servidor?: string;
    }>(`${API_BASE}/config/cliente/probar-cobis`, body);
  }

  startRun(escenarioId: string) {
    return this.http.post<RunResponse>(`${API_BASE}/run/${encodeURIComponent(escenarioId)}`, {});
  }

  getRun(runId: string) {
    return this.http.get<RunResponse>(`${API_BASE}/run/${encodeURIComponent(runId)}`);
  }

  cancelRun(runId: string) {
    return this.http.post<{ ok?: boolean; mensaje?: string; error?: string }>(
      `${API_BASE}/run/${encodeURIComponent(runId)}/cancel`,
      {}
    );
  }

  /**
   * ZIP de evidencia de una corrida como blob + respuesta completa: sin esto, un 401/404/500
   * con cuerpo JSON se guardaba en disco con extensión .zip y el archivo no abría.
   */
  descargarZipCorrida(url: string) {
    return this.http.get(`${API_BASE}${url}`, {
      responseType: 'blob',
      observe: 'response'
    });
  }

  /** Genera Excel+ZIP una sola vez; devuelve token para descargas GET rápidas. */
  prepararPaqueteModulo(body: ModuloQaRequest) {
    return this.http.post<{
      ok?: boolean;
      token?: string;
      zipNombre?: string;
      excelNombre?: string;
      pasosEvidencia?: number;
      error?: string;
    }>(`${API_BASE}/run/modulo/paquete-qa`, body);
  }

  descargarPaqueteModulo(token: string, tipo: 'excel' | 'zip') {
    return this.http.get(`${API_BASE}/run/modulo/paquete-qa/${encodeURIComponent(token)}/${tipo}`, {
      responseType: 'blob',
      observe: 'response'
    });
  }

  /** Paquete QA desde casos.json completo (p. ej. SC-476 — 12 casos Xray). */
  prepararPaqueteDesdeCasosJson(ticket: string) {
    const tk = encodeURIComponent(ticket);
    return this.http.post<{
      ok?: boolean;
      token?: string;
      ticket?: string;
      zipNombre?: string;
      excelNombre?: string;
      pasosEvidencia?: number;
      casos?: number;
      error?: string;
    }>(`${API_BASE}/run/ticket/${tk}/paquete-json`, {});
  }

  /** @deprecated Usar prepararPaqueteModulo + descargarPaqueteModulo */
  generarZipModulo(body: ModuloQaRequest) {
    return this.http.post(`${API_BASE}/run/modulo/evidencias.zip`, body, {
      responseType: 'blob',
      observe: 'response'
    });
  }

  /** Excel QA con hojas Casos + Evidencias (PNG embebidos desde las corridas del módulo). */
  generarExcelModulo(body: ModuloQaRequest) {
    return this.http.post(`${API_BASE}/run/modulo/excel`, body, {
      responseType: 'blob',
      observe: 'response'
    });
  }

  getUltimaCorrida() {
    return this.http.get<{ ok?: boolean; hayUltima?: boolean; corrida?: RunResponse }>(
      `${API_BASE}/run/ultima`
    );
  }

  getUltimaCorridaEscenario(escenarioId: string) {
    const id = encodeURIComponent(escenarioId);
    return this.http.get<{
      ok?: boolean;
      hayUltima?: boolean;
      escenarioId?: string;
      corrida?: RunResponse;
    }>(`${API_BASE}/run/escenario/${id}/ultima`);
  }

  /** Informe HTML vía header de auth (sin token en query). */
  getInformeHtml(path: string) {
    const clean = String(path || '').split('?')[0];
    return this.http.get(`${API_BASE}${clean}`, {
      responseType: 'text'
    });
  }

  getPruebasEscenarios() {
    return this.http.get<{ grupos?: any[]; casos?: any[]; regresionPruebas?: number }>(
      `${API_BASE}/pruebas/escenarios`
    );
  }

  getRelease9Info() {
    return this.http.get<{
      ok?: boolean;
      error?: string;
      release?: number;
      nota?: string;
      lotePrincipal?: string;
      automatizados?: number;
      manuales?: number;
      ticketsAutomatizacion?: { id?: string; titulo?: string }[];
      ticketsSoloExcel?: { id?: string; tipo?: string; titulo?: string }[];
    }>(`${API_BASE}/pruebas/release9-info`);
  }

  analizarPruebas(form: FormData) {
    return this.http.post<any>(`${API_BASE}/pruebas/analizar`, form);
  }

  probarJira(body: { jiraUrl: string; jiraEmail: string; jiraToken: string }) {
    return this.http.post<{
      ok?: boolean;
      error?: string;
      key?: string;
      titulo?: string;
      estado?: string;
      aviso?: string;
      mensaje?: string;
    }>(`${API_BASE}/pruebas/jira/probar`, body);
  }

  guardarPruebas(body: unknown) {
    return this.http.post<any>(`${API_BASE}/pruebas/guardar`, body);
  }

  /** Crea módulo(s) + varios .feature desde un paquete Gherkin (# Modulo: + Feature + ---). */
  importarLotePruebas(body: {
    contenido: string;
    forzarIncompleto?: boolean;
    confirmarSobrescribir?: boolean;
    moduloDefault?: string;
  }) {
    return this.http.post<any>(`${API_BASE}/pruebas/importar-lote`, body);
  }

  private encodeFeaturePath(nombre: string): string {
    return String(nombre || '')
      .replace(/\\/g, '/')
      .split('/')
      .filter(Boolean)
      .map((s) => encodeURIComponent(s))
      .join('/');
  }

  getPruebasBorrador(nombre: string) {
    return this.http.get<any>(`${API_BASE}/pruebas/borradores/${this.encodeFeaturePath(nombre)}`);
  }

  deletePruebasBorrador(nombre: string) {
    return this.http.delete<any>(`${API_BASE}/pruebas/borradores/${this.encodeFeaturePath(nombre)}`);
  }

  putPruebasGrupo(id: string, body: { nombre: string }) {
    return this.http.put<any>(`${API_BASE}/pruebas/grupos/${encodeURIComponent(id)}`, body);
  }

  postPruebasGrupo(body: { nombre: string; sub?: string }) {
    return this.http.post<any>(`${API_BASE}/pruebas/grupos`, body);
  }

  deletePruebasGrupo(id: string, eliminarCasos: boolean) {
    const qs = eliminarCasos ? '?eliminarCasos=true' : '';
    return this.http.delete<any>(`${API_BASE}/pruebas/grupos/${encodeURIComponent(id)}${qs}`);
  }

  getPruebasDeshacer() {
    return this.http.get<any>(`${API_BASE}/pruebas/deshacer`);
  }

  postPruebasDeshacer() {
    return this.http.post<any>(`${API_BASE}/pruebas/deshacer`, {});
  }

  getPruebasMapa() {
    return this.http.get<any>(`${API_BASE}/pruebas/mapa`);
  }

  postPruebasMapaReentrenar() {
    return this.http.post<any>(`${API_BASE}/pruebas/mapa/reentrenar`, {});
  }

  getManual() {
    return this.http.get<{ content?: string; ok?: boolean }>(`${API_BASE}/manual`);
  }

  getCatalogoSot() {
    return this.http.get<{
      ok?: boolean;
      config?: any;
      catalogo?: any;
      rutaCatalogo?: string;
      rutaConfig?: string;
    }>(`${API_BASE}/catalogo-sot`);
  }

  postCatalogoSotConfig(body: unknown) {
    return this.http.post<{
      ok?: boolean;
      error?: string;
      mensaje?: string;
      config?: any;
      tienePatTfs?: boolean;
    }>(`${API_BASE}/catalogo-sot/config`, body);
  }

  syncCatalogoSot(pull = true) {
    const qs = pull ? '' : '?pull=false';
    return this.http.post<{
      ok?: boolean;
      error?: string;
      mensaje?: string;
      generadoUtc?: string;
      resumenCorto?: string;
      repos?: any[];
    }>(`${API_BASE}/catalogo-sot/sync${qs}`, {});
  }

  getSuiteFeatureByTag(tag: string) {
    return this.http.get<{ ok?: boolean; contenido?: string; error?: string }>(
      `${API_BASE}/suite/feature-by-tag?tag=${encodeURIComponent(tag)}`
    );
  }

  getRunnerIaProyecto() {
    return this.http.get<{
      ok?: boolean;
      iaAlcance?: string;
      iaAlcanceTexto?: string;
      intranetAviso?: string;
      proyectoActivoId?: string;
      proyectoActivo?: any;
      proyectos?: any[];
      checklistDemo?: Array<{ id: string; texto: string }>;
    }>(`${API_BASE}/runner-ia/proyecto`);
  }

  postRunnerIaProyecto(body: unknown) {
    return this.http.post<{
      ok?: boolean;
      error?: string;
      mensaje?: string;
      proyectoActivoId?: string;
      proyectoActivo?: any;
      proyectos?: any[];
    }>(`${API_BASE}/runner-ia/proyecto`, body);
  }

  authStatus() {
    return this.http.get<{
      ok?: boolean;
      autenticado?: boolean;
      pinConfigurado?: boolean;
      pinVencido?: boolean;
      pinVencimientoHabilitado?: boolean;
      pinPredeterminado?: string;
      diasCaducidadPin?: number | null;
      diasRestantesPin?: number | null;
      recoveryEmailConfigurado?: boolean;
      recoveryEmailMascara?: string;
      authObligatorio?: boolean;
      intranetAviso?: string;
    }>(`${API_BASE}/auth/status`);
  }

  authSetup(pin: string, pinConfirmacion?: string) {
    return this.http.post<{
      ok?: boolean;
      token?: string;
      error?: string;
      mensaje?: string;
      debeCambiarPin?: boolean;
    }>(`${API_BASE}/auth/setup`, { pin, pinConfirmacion: pinConfirmacion ?? pin });
  }

  authLogin(pin: string) {
    return this.http.post<{
      ok?: boolean;
      token?: string;
      error?: string;
      mensaje?: string;
      debeCambiarPin?: boolean;
    }>(`${API_BASE}/auth/login`, { pin });
  }

  authLogout() {
    return this.http.post<{ ok?: boolean }>(`${API_BASE}/auth/logout`, {});
  }

  authCambiarPin(pinActual: string, pinNuevo: string, pinConfirmacion: string) {
    return this.http.post<{ ok?: boolean; error?: string; mensaje?: string }>(
      `${API_BASE}/auth/cambiar-pin`,
      { pinActual, pinNuevo, pinConfirmacion }
    );
  }

  authRecuperarEnviar(email?: string) {
    return this.http.post<{ ok?: boolean; error?: string; mensaje?: string }>(
      `${API_BASE}/auth/recuperar/enviar`,
      { email: email || '' }
    );
  }

  authRecuperarRestablecer(codigo: string, pinNuevo: string, pinConfirmacion: string) {
    return this.http.post<{ ok?: boolean; error?: string; mensaje?: string }>(
      `${API_BASE}/auth/recuperar/restablecer`,
      { codigo, pinNuevo, pinConfirmacion }
    );
  }

  mapeoEstado() {
    return firstValueFrom(
      this.http.get<{
        ok?: boolean;
        activa?: boolean;
        sesionId?: string;
        urlActual?: string;
        eventos?: number;
      }>(`${API_BASE}/mapeo-ui/estado`)
    );
  }

  mapeoUrlDefault(proyectoId?: string) {
    const q = proyectoId ? `?proyectoId=${encodeURIComponent(proyectoId)}` : '';
    return firstValueFrom(
      this.http.get<{
        ok?: boolean;
        url?: string;
        proyectoId?: string;
        proyectoNombre?: string;
        plantilla?: string;
        loginAutomaticoSot?: boolean;
        loginAutomaticoDefault?: boolean;
        usuario?: string;
        sucursalObjetivo?: string;
        codigoSucursal?: string;
        nota?: string;
        error?: string;
      }>(`${API_BASE}/mapeo-ui/url-default${q}`)
    );
  }

  mapeoIniciar(
    url: string,
    headless = false,
    opts?: { proyectoId?: string; loginAutomatico?: boolean }
  ) {
    return firstValueFrom(
      this.http.post<{
        ok?: boolean;
        activa?: boolean;
        sesionId?: string;
        proyectoId?: string;
        proyectoNombre?: string;
        plantilla?: string;
        loginAutomaticoSot?: boolean;
        urlInicio?: string;
        urlActual?: string;
        avisosLogin?: string[];
        error?: string;
      }>(`${API_BASE}/mapeo-ui/iniciar`, {
        url,
        headless,
        proyectoId: opts?.proyectoId,
        loginAutomatico: opts?.loginAutomatico
      })
    );
  }

  mapeoDetener() {
    return firstValueFrom(
      this.http.post<{
        ok?: boolean;
        error?: string;
        mensaje?: string;
        sesionId?: string;
        guardadoEn?: string;
        guardadoAbsoluto?: string;
        totalEventos?: number;
        gherkinPreview?: string;
      }>(`${API_BASE}/mapeo-ui/detener`, {})
    );
  }

  mapeoUltimaSesion() {
    return firstValueFrom(
      this.http.get<{
        ok?: boolean;
        disponible?: boolean;
        guardadoEn?: string;
        sesionId?: string;
        totalEventos?: number;
        gherkin?: string;
        eventos?: MapeoEventoUi[];
        json?: Record<string, unknown>;
      }>(`${API_BASE}/mapeo-ui/ultima-sesion`)
    );
  }

  mapeoLiberar() {
    return firstValueFrom(this.http.post<{ ok?: boolean; error?: string; mensaje?: string }>(`${API_BASE}/mapeo-ui/liberar`, {}));
  }

  mapeoEventos(desde = 0) {
    const q = desde > 0 ? `?desde=${desde}` : '';
    return firstValueFrom(
      this.http.get<{ ok?: boolean; activa?: boolean; total?: number; eventos?: MapeoEventoUi[] }>(
        `${API_BASE}/mapeo-ui/eventos${q}`
      )
    );
  }

  mapeoBuscarCodigo(consulta: string, xpath = '') {
    return firstValueFrom(
      this.http.post<{
        ok?: boolean;
        total?: number;
        coincidencias?: Array<{
          repoId?: string;
          archivo?: string;
          linea?: number;
          fragmento?: string;
          termino?: string;
        }>;
      }>(`${API_BASE}/mapeo-ui/buscar-codigo`, { consulta, xpath })
    );
  }

  mapeoExportarUrl(): string {
    return `${API_BASE}/mapeo-ui/exportar.json`;
  }
}

export interface MapeoEventoUi {
  orden?: number;
  xpath?: string;
  xpathUtil?: string;
  xpathPlaywright?: string;
  tag?: string;
  texto?: string;
  id?: string;
  clases?: string;
  href?: string;
  url?: string;
  coincidenciasCodigo?: Array<{
    repoId?: string;
    archivo?: string;
    linea?: number;
    fragmento?: string;
    termino?: string;
    puntaje?: number;
  }>;
}
