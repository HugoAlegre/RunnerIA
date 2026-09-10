import { Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiService } from './api.service';
import { ToastService } from './toast.service';
import { RunResponse, ColaItem } from '../models';
import {
  esCorridaDetenida,
  esCorridaVacia,
  esErrorProceso,
  mensajeCorrida
} from '../run-outcome';

const LS_KEY = 'sot-runner-ultima-corrida';
const LS_HIST = 'sot-runner-hist-escenarios';
const LS_POR_ESC = 'sot-runner-ultimas-por-escenario';
const SS_COLA = 'sot-runner-cola';

export type RunFase = 'idle' | 'loading' | 'running' | 'ok' | 'fail' | 'empty';

@Injectable({ providedIn: 'root' })
export class RunService {
  readonly ultima = signal<RunResponse | null>(null);
  /** Última corrida del escenario seleccionado (puede diferir de la global). */
  readonly ultimaEscenario = signal<RunResponse | null>(null);
  readonly enCurso = signal(false);
  readonly runIdActual = signal<string | null>(null);
  readonly log = signal('');
  readonly fase = signal<RunFase>('idle');
  readonly faseTitulo = signal('');
  readonly faseSub = signal('');
  /** Leyenda de cierre: «Finalizó · fecha/hora» (panel de progreso). */
  readonly finEtiqueta = signal('');
  readonly terminadaTick = signal(0);
  readonly histEscenarios = signal<Record<string, 'ok' | 'fail'>>(this.leerHist());
  readonly loteCancelado = signal(false);
  /** Cola FIFO de escenarios pendientes de ejecución. */
  readonly cola = signal<ColaItem[]>(this.leerCola());

  private pollTimer: ReturnType<typeof setInterval> | null = null;
  private zipDescargadoId: string | null = null;
  private informeBlobUrl: string | null = null;
  private procesandoCola = false;
  /** Evita auto-dequeue durante regresión de módulo (lote interno). */
  private bloquearColaAuto = false;
  /** Tras finalizar corrida individual, generar Excel QA (Runner page registra el handler). */
  entregaQaHandler: ((escenarioId: string, titulo: string, data: RunResponse) => Promise<void>) | null =
    null;
  private entregaQaCorridaActual = false;

  constructor(
    private api: ApiService,
    private toast: ToastService
  ) {
    const saved = this.leerGuardada();
    if (saved) this.aplicarUltima(saved, { sinDescargaAuto: true, desdeCache: true });
  }

  estadoEscenario(id: string): 'ok' | 'fail' | 'none' {
    return this.histEscenarios()[id] || 'none';
  }

  /** true si el escenario ya está en cola o corriendo ahora. */
  estaPendienteOEnCurso(escenarioId: string): boolean {
    if (this.enCurso() && this.ultima()?.escenarioId === escenarioId) return true;
    return this.cola().some((c) => c.escenarioId === escenarioId);
  }

  setBloquearColaAuto(bloquear: boolean): void {
    this.bloquearColaAuto = bloquear;
  }

  encolar(escenarioId: string, titulo: string): ColaItem | null {
    if (this.estaPendienteOEnCurso(escenarioId)) return null;
    const item: ColaItem = {
      colaId: `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
      escenarioId,
      titulo,
      encoladoEn: Date.now()
    };
    const next = [...this.cola(), item];
    this.cola.set(next);
    this.persistirCola(next);
    const pos = (this.enCurso() ? 1 : 0) + next.length;
    this.toast.info(`Encolado (#${pos})`);
    if (!this.enCurso()) void this.procesarSiguienteEnCola();
    return item;
  }

  encolarVarios(items: Array<{ escenarioId: string; titulo: string }>): number {
    const toAdd = items.filter((it) => !this.estaPendienteOEnCurso(it.escenarioId));
    if (!toAdd.length) return 0;
    const nuevos: ColaItem[] = toAdd.map((it) => ({
      colaId: `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
      escenarioId: it.escenarioId,
      titulo: it.titulo,
      encoladoEn: Date.now()
    }));
    const next = [...this.cola(), ...nuevos];
    this.cola.set(next);
    this.persistirCola(next);
    if (!this.enCurso()) void this.procesarSiguienteEnCola();
    return nuevos.length;
  }

  quitarDeCola(colaId: string): void {
    const next = this.cola().filter((c) => c.colaId !== colaId);
    this.cola.set(next);
    this.persistirCola(next);
  }

  vaciarCola(): void {
    this.cola.set([]);
    this.persistirCola([]);
  }

  private leerCola(): ColaItem[] {
    try {
      const raw = sessionStorage.getItem(SS_COLA);
      if (!raw) return [];
      const arr = JSON.parse(raw);
      return Array.isArray(arr) ? arr : [];
    } catch {
      return [];
    }
  }

  private persistirCola(items: ColaItem[]): void {
    try {
      if (items.length) sessionStorage.setItem(SS_COLA, JSON.stringify(items));
      else sessionStorage.removeItem(SS_COLA);
    } catch {
      /* ignore */
    }
  }

  private async procesarSiguienteEnCola(): Promise<void> {
    if (this.procesandoCola || this.bloquearColaAuto || this.enCurso()) return;
    const items = this.cola();
    if (!items.length) return;
    this.procesandoCola = true;
    const [next, ...rest] = items;
    this.cola.set(rest);
    this.persistirCola(rest);
    try {
      await this.ejecutar(next.escenarioId, next.titulo, { desdeCola: true, entregaQa: true });
    } finally {
      this.procesandoCola = false;
    }
  }

  async esperarFinCorrida(timeoutMs = 45_000): Promise<boolean> {
    const start = Date.now();
    while (this.enCurso() && Date.now() - start < timeoutMs) {
      await new Promise((r) => setTimeout(r, 500));
    }
    return !this.enCurso();
  }

  async detenerYEjecutar(
    escenarioId: string,
    titulo: string,
    opts?: { entregaQa?: boolean }
  ): Promise<void> {
    await this.detener();
    const ok = await this.esperarFinCorrida();
    if (!ok) {
      this.toast.warn('No se pudo detener la corrida a tiempo');
      return;
    }
    await this.ejecutar(escenarioId, titulo, { entregaQa: opts?.entregaQa });
  }

  /** Reanuda la cola tras un lote interno o liberación manual. */
  reanudarCola(): void {
    void this.procesarSiguienteEnCola();
  }

  /** Fecha/hora legible de la última corrida guardada del escenario (sidebar). */
  ultimaCorridaTexto(escenarioId: string): string | null {
    const c = this.leerPorEscenario(escenarioId);
    return this.formatearFechaCorrida(c?.finalizado);
  }

  /** Corrida a mostrar/descargar: en curso usa la global; si no, la del escenario. */
  corridaParaEvidencia(escenarioId?: string | null): RunResponse | null {
    const u = this.ultima();
    if (this.enCurso() && u) return u;
    const esc = this.ultimaEscenario();
    if (escenarioId && esc?.escenarioId === escenarioId) return esc;
    if (escenarioId && u?.escenarioId === escenarioId) return u;
    return esc || u;
  }

  /** Limpia estado local si el API ya no tiene corrida activa. */
  liberarCorridaLocal(): void {
    if (this.pollTimer) clearInterval(this.pollTimer);
    this.pollTimer = null;
    this.enCurso.set(false);
    this.runIdActual.set(null);
    this.api.busy.set(false);
    this.faseSub.set('Estado local liberado. Podés ejecutar otra corrida.');
  }

  async recuperarEstadoSiAtascado(): Promise<void> {
    if (!this.enCurso()) return;
    try {
      const r = await firstValueFrom(this.api.getUltimaCorrida());
      const corrida = r?.corrida;
      if (!r?.hayUltima || corrida?.estado === 'finalizado') {
        this.liberarCorridaLocal();
        if (corrida?.runId) this.aplicarUltima(corrida, { sinDescargaAuto: true });
      }
    } catch {
      if (!this.runIdActual()) this.liberarCorridaLocal();
    }
  }

  async restaurarUltima(): Promise<void> {
    try {
      const r = await firstValueFrom(this.api.getUltimaCorrida());
      if (r?.hayUltima && r.corrida?.runId) {
        this.aplicarUltima(r.corrida, { sinDescargaAuto: true });
        return;
      }
    } catch {
      /* offline */
    }
    const saved = this.leerGuardada();
    if (saved) this.aplicarUltima(saved, { sinDescargaAuto: true, desdeCache: true });
  }

  /** Carga la última ejecución guardada de un escenario (API + caché local). */
  async cargarUltimaDeEscenario(escenarioId: string | null | undefined): Promise<void> {
    if (!escenarioId) {
      this.ultimaEscenario.set(null);
      return;
    }
    if (this.enCurso() && this.ultima()?.escenarioId === escenarioId) {
      this.ultimaEscenario.set(this.ultima());
      return;
    }
    try {
      const r = await firstValueFrom(this.api.getUltimaCorridaEscenario(escenarioId));
      if (r?.hayUltima && r.corrida?.runId) {
        this.ultimaEscenario.set(r.corrida);
        this.persistirPorEscenario(escenarioId, r.corrida);
        if (r.corrida.ok === true || r.corrida.ok === false) {
          this.guardarHist(escenarioId, r.corrida.ok ? 'ok' : 'fail');
        }
        return;
      }
    } catch {
      /* offline / 401 */
    }
    const cached = this.leerPorEscenario(escenarioId);
    this.ultimaEscenario.set(cached);
  }

  private aplicarUltima(
    data: RunResponse,
    opts?: { sinDescargaAuto?: boolean; desdeCache?: boolean }
  ): void {
    this.ultima.set(data);
    this.persistir(data);
    if (data.escenarioId) {
      this.persistirPorEscenario(data.escenarioId, data);
      this.ultimaEscenario.set(data);
    }
    if (data.escenarioId && (data.ok === true || data.ok === false)) {
      this.guardarHist(data.escenarioId, data.ok ? 'ok' : 'fail');
    }
    if (data.log) this.log.set(data.log);
    if (data.estado === 'finalizado' || data.ok === true || data.ok === false) {
      this.aplicarFaseFinal(data, undefined, opts?.desdeCache);
    }
  }

  private aplicarFaseFinal(
    data: RunResponse,
    progreso?: string,
    desdeCache?: boolean
  ): void {
    const tituloPrueba = (data.titulo || '').trim() || 'Prueba';
    const cuando = this.formatearFechaCorrida(data.finalizado);
    const etiquetaFin = cuando
      ? `${desdeCache ? 'Última corrida' : 'Finalizó'} · ${cuando}`
      : desdeCache
        ? 'Última corrida'
        : 'Finalizó';

    if (esCorridaVacia(data)) {
      this.fase.set('empty');
      this.finEtiqueta.set(etiquetaFin);
      this.faseTitulo.set(tituloPrueba);
      this.faseSub.set(
        (progreso ? progreso + ' · ' : '') +
          (mensajeCorrida(data) || 'No se ejecutó ningún escenario.')
      );
      return;
    }
    if (data.ok) {
      this.fase.set('ok');
      this.finEtiqueta.set(etiquetaFin);
      this.faseTitulo.set(tituloPrueba);
      this.faseSub.set(progreso || '');
      return;
    }
    const detenida = esCorridaDetenida(data);
    const proceso = esErrorProceso(data);
    this.fase.set('fail');
    this.finEtiqueta.set(etiquetaFin);
    this.faseTitulo.set(tituloPrueba);
    const detalle =
      mensajeCorrida(data) ||
      (detenida ? 'Corrida detenida.' : proceso ? 'Error al ejecutar la prueba.' : 'Revisá el informe.');
    this.faseSub.set(progreso ? `${progreso} · ${detalle}` : detalle);
  }

  private formatearFechaCorrida(finalizado?: string | null): string | null {
    if (!finalizado) return null;
    const d = new Date(finalizado);
    if (Number.isNaN(d.getTime())) return null;
    return d.toLocaleString('es-AR', { dateStyle: 'short', timeStyle: 'short' });
  }

  private leerHist(): Record<string, 'ok' | 'fail'> {
    try {
      const raw = localStorage.getItem(LS_HIST);
      if (!raw) return {};
      const d = JSON.parse(raw);
      return d && typeof d === 'object' ? d : {};
    } catch {
      return {};
    }
  }

  private guardarHist(escenarioId: string, estado: 'ok' | 'fail'): void {
    const next = { ...this.histEscenarios(), [escenarioId]: estado };
    this.histEscenarios.set(next);
    try {
      localStorage.setItem(LS_HIST, JSON.stringify(next));
    } catch {
      /* ignore */
    }
  }

  private leerGuardada(): RunResponse | null {
    try {
      const raw = localStorage.getItem(LS_KEY) || sessionStorage.getItem(LS_KEY);
      if (!raw) return null;
      const d = JSON.parse(raw);
      return d?.runId ? d : null;
    } catch {
      return null;
    }
  }

  private leerPorEscenario(escenarioId: string): RunResponse | null {
    try {
      const raw = localStorage.getItem(LS_POR_ESC);
      if (!raw) return null;
      const map = JSON.parse(raw) as Record<string, RunResponse>;
      const d = map?.[escenarioId];
      return d?.runId ? d : null;
    } catch {
      return null;
    }
  }

  private persistirPorEscenario(escenarioId: string, data: RunResponse): void {
    try {
      const raw = localStorage.getItem(LS_POR_ESC);
      const map = (raw ? JSON.parse(raw) : {}) as Record<string, RunResponse>;
      const slim: RunResponse = {
        runId: data.runId,
        escenarioId: data.escenarioId || escenarioId,
        titulo: data.titulo,
        estado: data.estado,
        ok: data.ok,
        corridaVacia: data.corridaVacia,
        fallasEscenarios: data.fallasEscenarios,
        errorProceso: data.errorProceso,
        exitCode: data.exitCode,
        error: data.error,
        iniciado: data.iniciado,
        finalizado: data.finalizado,
        tieneEvidencia: data.tieneEvidencia,
        evidenciaZipUrl: data.evidenciaZipUrl,
        evidenciaZipNombre: data.evidenciaZipNombre,
        evidenciaInformeUrl: data.evidenciaInformeUrl,
        evidenciaZipEscenarioUrl: data.evidenciaZipEscenarioUrl,
        evidenciaInformeEscenarioUrl: data.evidenciaInformeEscenarioUrl
      };
      map[escenarioId] = slim;
      localStorage.setItem(LS_POR_ESC, JSON.stringify(map));
    } catch {
      /* ignore */
    }
  }

  private persistir(data: RunResponse | null): void {
    try {
      if (data?.runId) {
        localStorage.setItem(LS_KEY, JSON.stringify(data));
        try {
          sessionStorage.removeItem(LS_KEY);
        } catch {
          /* ignore */
        }
      } else localStorage.removeItem(LS_KEY);
    } catch {
      /* ignore */
    }
  }

  nombreZip(data: RunResponse): string {
    if (data.evidenciaZipNombre) return data.evidenciaZipNombre;
    const titulo = String(data.titulo || data.escenarioId || 'corrida')
      .replace(/[<>:"/\\|?*\x00-\x1F]/g, '_')
      .replace(/\s+/g, '_')
      .slice(0, 80);
    const d = new Date(data.finalizado || data.iniciado || Date.now());
    const stamp =
      d.getFullYear() +
      '-' +
      String(d.getMonth() + 1).padStart(2, '0') +
      '-' +
      String(d.getDate()).padStart(2, '0') +
      '_' +
      String(d.getHours()).padStart(2, '0') +
      '-' +
      String(d.getMinutes()).padStart(2, '0') +
      '-' +
      String(d.getSeconds()).padStart(2, '0');
    return `${titulo}_${stamp}.zip`;
  }

  pathZip(data: RunResponse): string {
    return (
      data.evidenciaZipEscenarioUrl ||
      data.evidenciaZipUrl ||
      (data.runId ? `/run/${data.runId}/evidencia.zip` : '')
    ).split('?')[0];
  }

  pathInforme(data: RunResponse): string {
    return (
      data.evidenciaInformeEscenarioUrl ||
      data.evidenciaInformeUrl ||
      (data.runId ? `/run/${data.runId}/informe` : '')
    ).split('?')[0];
  }

  async descargarZip(data: RunResponse, forzar = false): Promise<string | null> {
    if (!data.tieneEvidencia || !data.runId) return null;
    if (!forzar && this.zipDescargadoId === data.runId) return null;
    this.zipDescargadoId = data.runId;

    const path = this.pathZip(data);
    if (!path) return 'No hay URL de ZIP para esta corrida.';
    try {
      const resp = await firstValueFrom(this.api.descargarZipCorrida(path));
      const blob = resp.body;
      if (!blob || blob.size === 0) throw new Error('El servidor devolvió un ZIP vacío.');
      if (blob.type.includes('json') || blob.type.startsWith('text/'))
        throw new Error(await this.motivoErrorBlob(blob));

      this.guardarArchivo(blob, this.nombreZip(data));
      return null;
    } catch (err: any) {
      this.zipDescargadoId = null;
      const motivo = String(err?.message || err || 'No se pudo descargar el ZIP.');
      this.log.update((l) => (l ? `${l}\n[Runner] ZIP: ${motivo}` : `[Runner] ZIP: ${motivo}`));
      return motivo;
    }
  }

  /** Abre el informe HTML (auth por header → blob, sin token en URL). */
  async abrirInforme(data: RunResponse, modo: 'pestaña' | 'blobUrl'): Promise<string | null> {
    const path = this.pathInforme(data);
    if (!path || !data.tieneEvidencia) return 'No hay informe HTML de esta corrida.';
    try {
      const html = await firstValueFrom(this.api.getInformeHtml(path));
      if (!html || html.trim().startsWith('{')) {
        try {
          const j = JSON.parse(html);
          return String(j?.error || 'No se encontró el informe HTML.');
        } catch {
          return 'No se encontró el informe HTML.';
        }
      }
      const blob = new Blob([html], { type: 'text/html;charset=utf-8' });
      if (this.informeBlobUrl) URL.revokeObjectURL(this.informeBlobUrl);
      this.informeBlobUrl = URL.createObjectURL(blob);
      if (modo === 'pestaña') {
        window.open(this.informeBlobUrl, '_blank', 'noopener');
      }
      return null;
    } catch (err: any) {
      return String(err?.error?.error || err?.message || 'No se pudo cargar el informe.');
    }
  }

  informeBlobHref(): string {
    return this.informeBlobUrl || '';
  }

  private async motivoErrorBlob(blob: Blob): Promise<string> {
    try {
      const texto = await blob.text();
      const json = JSON.parse(texto);
      return String(json?.error || texto).slice(0, 300);
    } catch {
      return 'El servidor respondió un error en lugar del ZIP.';
    }
  }

  /** Descarga un blob al disco del operador (nombre de archivo con extensión). */
  descargarArchivo(blob: Blob, nombre: string): void {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = nombre;
    a.rel = 'noopener';
    a.style.display = 'none';
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 30_000);
  }

  private guardarArchivo(blob: Blob, nombre: string): void {
    this.descargarArchivo(blob, nombre);
  }

  private async finalizarCorrida(
    data: RunResponse,
    escenarioId: string,
    titulo: string,
    opts?: { descargarZip?: boolean; progreso?: string; entregaQa?: boolean }
  ): Promise<void> {
    const pedirQa = !!opts?.entregaQa;
    if (!data.escenarioId) data.escenarioId = escenarioId;
    if (!data.titulo) data.titulo = titulo;
    this.ultima.set(data);
    this.ultimaEscenario.set(data);
    this.persistir(data);
    this.persistirPorEscenario(data.escenarioId || escenarioId, data);
    this.guardarHist(data.escenarioId || escenarioId, data.ok ? 'ok' : 'fail');
    this.aplicarFaseFinal(data, opts?.progreso);
    if (opts?.descargarZip !== false && !pedirQa && data.tieneEvidencia) {
      const zipErr = await this.descargarZip(data);
      if (zipErr) this.toast.warn(`ZIP: ${zipErr}`);
    }
    this.terminadaTick.update((n) => n + 1);
    if (pedirQa && !this.bloquearColaAuto && this.entregaQaHandler) {
      try {
        await this.entregaQaHandler(escenarioId, titulo, data);
      } catch (err: any) {
        this.toast.warn(`Excel QA: ${String(err?.message || err || 'No se pudo generar')}`);
      }
    }
    if (!this.bloquearColaAuto) {
      void this.procesarSiguienteEnCola();
    }
  }

  async ejecutar(
    escenarioId: string,
    titulo: string,
    opts?: { desdeCola?: boolean; entregaQa?: boolean }
  ): Promise<void> {
    if (this.enCurso()) {
      if (!opts?.desdeCola) this.toast.warn('Ya hay una corrida en curso');
      return;
    }
    this.entregaQaCorridaActual = !!opts?.entregaQa;
    this.loteCancelado.set(false);
    this.enCurso.set(true);
    this.api.busy.set(true);
    this.log.set('');
    this.zipDescargadoId = null;
    this.finEtiqueta.set('');
    this.fase.set('loading');
    this.faseTitulo.set(titulo);
    this.faseSub.set('Preparando la corrida…');

    try {
      const data = await firstValueFrom(this.api.startRun(escenarioId));
      this.runIdActual.set(data.runId || null);
      this.fase.set('running');
      this.faseSub.set('La prueba está corriendo…');
      this.iniciarPolling(data.runId!, titulo, escenarioId);
    } catch (err: any) {
      this.enCurso.set(false);
      this.runIdActual.set(null);
      this.api.busy.set(false);
      this.fase.set('fail');
      this.faseTitulo.set('Salió con fallas');
      const msg = err?.error?.error || err?.message || 'Error al iniciar';
      this.faseSub.set(msg);
      this.log.set(String(msg));
      this.toast.error(msg);
      const prev = this.leerGuardada();
      if (prev) this.aplicarUltima(prev, { sinDescargaAuto: true, desdeCache: true });
      void this.procesarSiguienteEnCola();
    }
  }

  async detener(): Promise<void> {
    this.loteCancelado.set(true);
    const runId = this.runIdActual() || this.ultima()?.runId;
    if (!runId) {
      this.faseSub.set('No hay corrida activa para detener.');
      return;
    }
    try {
      const r = await firstValueFrom(this.api.cancelRun(runId));
      this.faseSub.set(r?.mensaje || 'Deteniendo…');
      this.log.update((l) => (l ? l + '\n' : '') + '[UI] Pedido de detener enviado.');
    } catch (err: any) {
      this.faseSub.set(err?.error?.error || err?.message || 'No se pudo detener');
    }
  }

  ejecutarYEsperar(
    escenarioId: string,
    titulo: string,
    opts?: { descargarZip?: boolean; progreso?: string; entregaQa?: boolean }
  ): Promise<RunResponse> {
    return new Promise(async (resolve, reject) => {
      if (this.enCurso()) {
        reject(new Error('Ya hay una corrida en curso.'));
        return;
      }
      if (this.loteCancelado()) {
        reject(new Error('Lote detenido por el operador.'));
        return;
      }
      this.enCurso.set(true);
      this.api.busy.set(true);
      this.log.set('');
      this.zipDescargadoId = null;
      this.finEtiqueta.set('');
      this.fase.set('loading');
      this.faseTitulo.set(titulo);
      this.faseSub.set(opts?.progreso || 'Preparando la corrida…');

      try {
        const data = await firstValueFrom(this.api.startRun(escenarioId));
        this.runIdActual.set(data.runId || null);
        this.fase.set('running');
        this.faseSub.set(opts?.progreso || 'La prueba está corriendo…');
        if (this.pollTimer) clearInterval(this.pollTimer);
        this.pollTimer = setInterval(async () => {
          try {
            const st = await firstValueFrom(this.api.getRun(data.runId!));
            if (st.log) this.log.set(st.log);
            if (st.estado !== 'finalizado') return;

            if (this.pollTimer) clearInterval(this.pollTimer);
            this.pollTimer = null;
            this.enCurso.set(false);
            this.runIdActual.set(null);
            this.api.busy.set(false);
            await this.finalizarCorrida(st, escenarioId, titulo, {
              descargarZip: opts?.descargarZip,
              progreso: opts?.progreso,
              entregaQa: opts?.entregaQa
            });
            resolve(st);
          } catch (err: any) {
            if (this.pollTimer) clearInterval(this.pollTimer);
            this.pollTimer = null;
            this.enCurso.set(false);
            this.runIdActual.set(null);
            this.api.busy.set(false);
            this.fase.set('fail');
            reject(err);
          }
        }, 2000);
      } catch (err: any) {
        this.enCurso.set(false);
        this.runIdActual.set(null);
        this.api.busy.set(false);
        this.fase.set('fail');
        this.faseTitulo.set('Salió con fallas');
        this.faseSub.set(err?.error?.error || err?.message || 'Error al iniciar');
        reject(err);
      }
    });
  }

  private iniciarPolling(runId: string, titulo: string, escenarioId: string): void {
    if (this.pollTimer) clearInterval(this.pollTimer);
    this.pollTimer = setInterval(async () => {
      try {
        const data = await firstValueFrom(this.api.getRun(runId));
        if (data.log) this.log.set(data.log);
        if (data.estado !== 'finalizado') return;

        if (this.pollTimer) clearInterval(this.pollTimer);
        this.pollTimer = null;
        this.enCurso.set(false);
        this.runIdActual.set(null);
        this.api.busy.set(false);
        await this.finalizarCorrida(data, escenarioId, titulo, {
          descargarZip: !this.entregaQaCorridaActual,
          entregaQa: this.entregaQaCorridaActual
        });
        this.entregaQaCorridaActual = false;
      } catch (err: any) {
        if (this.pollTimer) clearInterval(this.pollTimer);
        this.pollTimer = null;
        this.enCurso.set(false);
        this.runIdActual.set(null);
        this.api.busy.set(false);
        this.fase.set('fail');
        this.faseTitulo.set('Salió con fallas');
        const status = err?.status;
        const msg =
          status === 404
            ? 'API reiniciada — la corrida puede seguir en segundo plano. Revisá carpeta Ejecucion_*.'
            : err?.message || 'Error consultando corrida';
        this.faseSub.set(msg);
        this.toast.warn(msg);
        void this.procesarSiguienteEnCola();
      }
    }, 2000);
  }
}
