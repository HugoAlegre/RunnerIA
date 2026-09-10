import {
  Component,
  computed,
  effect,
  HostListener,
  inject,
  OnInit,
  signal
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { CatalogService } from '../../core/services/catalog.service';
import { RunService } from '../../core/services/run.service';
import { ApiService } from '../../core/services/api.service';
import { ToastService } from '../../core/services/toast.service';
import { firstValueFrom } from 'rxjs';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { QUERY_REGRESION_COMPLETA } from '../../core/run-query';
import { ConfirmDialogComponent } from '../../shared/confirm-dialog/confirm-dialog.component';
import { RunConflictDialogComponent } from '../../shared/run-conflict-dialog/run-conflict-dialog.component';
import { RunConflictAction, RunResponse, Categoria } from '../../core/models';
import {
  esCorridaDetenida,
  esCorridaVacia,
  extraerObservacionDeLog,
  mensajeCorrida,
  resultadoObtenidoCorrida
} from '../../core/run-outcome';

@Component({
  selector: 'app-runner-page',
  standalone: true,
  imports: [CommonModule, FormsModule, ConfirmDialogComponent, RunConflictDialogComponent],
  templateUrl: './runner-page.component.html',
  styleUrl: './runner-page.component.scss'
})
export class RunnerPageComponent implements OnInit {
  readonly catalog = inject(CatalogService);
  readonly runs = inject(RunService);
  readonly api = inject(ApiService);
  readonly toast = inject(ToastService);
  private sanitizer = inject(DomSanitizer);
  private route = inject(ActivatedRoute);
  private router = inject(Router);

  modulosAbiertos = signal<string[]>([]);
  filtro = signal('');
  statusMsg = signal('');
  statusTipo = signal('info');
  ambiente = signal('Dev');
  /** SC-416 — cajas configuradas (live desde /config/cliente) */
  cajaCompensada = signal('4161');
  cajaNormal = signal('4162');
  cajaMiniBoveda = signal('(auto)');
  zipModuloUrl = signal<string | null>(null);
  zipModuloNombre = signal('');
  zipModuloBlob = signal<Blob | null>(null);
  excelModuloUrl = signal<string | null>(null);
  excelModuloNombre = signal('');
  excelModuloBlob = signal<Blob | null>(null);
  dialogOpen = signal(false);
  dialogTitle = signal('');
  dialogBody = signal('');
  dialogConfirmLabel = signal('Aceptar');
  conflictOpen = signal(false);
  conflictTituloActual = signal('');
  conflictTituloNueva = signal('');
  private conflictResolver: ((action: RunConflictAction) => void) | null = null;
  mostrarInformeEmbed = signal(false);
  /** Resumen Release 9 desde manifest.json (API). */
  release9Info = signal<{
    ok?: boolean;
    automatizados?: number;
    manuales?: number;
    nota?: string;
    lotePrincipal?: string;
    ticketsAutomatizacion?: { id?: string; titulo?: string }[];
    ticketsSoloExcel?: { id?: string; tipo?: string; titulo?: string }[];
  } | null>(null);
  private dialogResolver: ((ok: boolean) => void) | null = null;
  private lastTerminada = 0;
  private autoRunHandled = false;
  /** Evita que queryParams revertan la selección tras un clic local en el sidebar. */
  private sincronizandoQueryLocal = false;

  esc = computed(() => this.catalog.escenarioActual());
  cat = computed(() => this.catalog.categoriaActual());

  /** Muestra panel de cajas SC-416 cuando el escenario es de esa suite. */
  muestraCajasSc416 = computed(() => {
    const id = this.catalog.escenarioId() || '';
    const tag = String(this.esc()?.tag || '');
    return id.startsWith('32') || /SC-?416|CierreForzadoNota/i.test(tag);
  });

  /**
   * Pruebas a correr con «Ejecutar todos del módulo»:
   * excluye el agregado «(Todos)» si hay hijos CA/CN (para Excel fila por caso).
   */
  idsModuloEjecutables = computed(() => {
    const catId = this.catalog.categoriaId();
    const ids = this.catalog.escenariosVisibles();
    const map = this.catalog.escenarios();

    // Release 9: un solo lote = script run-Release9-Todos.ps1 (parte automatizada).
    if (catId === 'release-9' && ids.includes('r9-todo')) {
      return ['r9-todo'];
    }

    return ids.filter((id) => {
      const e = map[id];
      if (!e) return false;
      const tieneHijosCa = ids.some(
        (o) =>
          o !== id &&
          (o.startsWith(id + '-CA') ||
            o.startsWith(id + '-CN') ||
            o.startsWith(id + '_CA') ||
            o.startsWith(id + '_CN'))
      );
      if (tieneHijosCa) return false;
      if (e.regresionModulo || id.endsWith('-regresion')) return false;
      // Agregador «Correr todo» — solo al clic explícito en el caso de regresión del módulo.
      if (id === 'r9-todo' || id.endsWith('-todo') || id.endsWith('-ALL')) return false;
      return true;
    });
  });

  labelEjecutarTodosModulo = computed(() => {
    const n = this.idsModuloEjecutables().length;
    const nombre = this.cat()?.nombre || 'módulo';
    if (this.catalog.categoriaId() === 'release-9') {
      return `Correr todo Release 9 (${n})`;
    }
    if (this.catalog.categoriaId() === 'regresion') {
      return `Regresión 00 — Alta+Intercaja (${n})`;
    }
    return `Regresión del módulo «${nombre}» (${n})`;
  });

  /** Módulos del sidebar; con búsqueda deja solo los que tienen coincidencias. */
  modulosVisibles = computed(() => {
    const cats = this.catalog.categoriasVisibles();
    const q = this.filtro().trim().toLowerCase();
    if (!q) return cats;
    return cats.filter(
      (c) => c.nombre.toLowerCase().includes(q) || this.pruebasDe(c).length > 0
    );
  });

  countPruebasModulo = computed(() => {
    const cat = this.cat();
    if (!cat) return 0;
    return this.catalog.escenariosVisibles(cat).filter((id) => this.catalog.escenarios()[id]?.generada).length;
  });

  /** Última corrida del escenario seleccionado (o la en curso). */
  corridaEvidencia = computed(() =>
    this.runs.corridaParaEvidencia(this.catalog.escenarioId())
  );

  tieneInforme = computed(() => {
    const u = this.corridaEvidencia();
    if (!u?.tieneEvidencia || !u.runId) return false;
    return !!(u.evidenciaInformeEscenarioUrl || u.evidenciaInformeUrl || u.evidenciaInforme);
  });

  informeBlobSafe = signal<SafeResourceUrl | null>(null);

  zipUrl = computed(() => {
    const u = this.corridaEvidencia();
    if (!u?.tieneEvidencia || !u.runId) return '#';
    return this.runs.pathZip(u) || '#';
  });

  constructor() {
    effect(() => {
      const escId = this.catalog.escenarioId();
      void this.runs.cargarUltimaDeEscenario(escId);
      this.mostrarInformeEmbed.set(false);
      this.informeBlobSafe.set(null);
    });

    effect(() => {
      const tick = this.runs.terminadaTick();
      if (tick <= this.lastTerminada) return;
      this.lastTerminada = tick;
      const u = this.runs.ultima();
      if (u?.corridaVacia || esCorridaVacia(u!)) {
        this.toast.warn('Corrida sin pruebas ejecutadas — revisar filtro/tag');
      } else if (u?.ok) {
        this.toast.ok('Corrida OK');
      } else {
        this.toast.warn('Corrida con fallas');
      }
    });
  }

  async ngOnInit(): Promise<void> {
    await this.catalog.syncPruebas();

    const snap = this.route.snapshot.queryParamMap;
    const catInicial = snap.get('cat');
    const escInicial = snap.get('esc');
    if (catInicial) {
      this.aplicarSeleccionModulo(catInicial, escInicial);
    } else {
      this.modulosAbiertos.set([this.catalog.categoriaId()]);
    }

    await this.runs.restaurarUltima();
    await this.runs.recuperarEstadoSiAtascado();
    void this.cargarRelease9Info();
    try {
      const cfg = await firstValueFrom(this.api.getConfigCliente());
      this.ambiente.set(String(cfg.ambiente || 'Dev'));
      this.cajaCompensada.set(String(cfg.cierreForzadoNotaCompensada || '4161'));
      this.cajaNormal.set(String(cfg.cierreForzadoNotaNormal || '4162'));
      const mini = String(cfg.cierreForzadoNotaMiniBoveda || '').trim();
      this.cajaMiniBoveda.set(mini || '(auto: texto Mini/bóveda)');
    } catch {
      /* offline */
    }

    this.runs.entregaQaHandler = async (escenarioId, titulo, data) => {
      if (escenarioId === 'r9-SC-476-qa') {
        await this.descargarEntregaQaDesdeCasosJson('SC-476');
        return;
      }
      await this.generarEntregaQaDesdeCorrida(escenarioId, titulo, data);
    };

    this.route.queryParamMap.subscribe((q) => {
      if (this.sincronizandoQueryLocal) return;

      const cat = q.get('cat');
      const esc = q.get('esc');
      if (cat) {
        const catRes = this.catalog.resolveModuloId(cat);
        const curCat = this.catalog.categoriaId();
        if (catRes !== curCat) {
          this.aplicarSeleccionModulo(cat, esc, true);
        } else if (
          esc &&
          esc !== this.catalog.escenarioId() &&
          this.catalog.escenariosVisibles().includes(esc)
        ) {
          this.catalog.seleccionarEscenario(esc);
        }
      }

      if (q.get('run') === '1' && esc && !this.autoRunHandled) {
        this.autoRunHandled = true;
        void this.router.navigate([], {
          relativeTo: this.route,
          queryParams: { run: null },
          queryParamsHandling: 'merge',
          replaceUrl: true
        });
        const e = this.catalog.escenarios()[esc];
        if (!e) return;
        if (!this.api.online()) {
          this.toast.warn('Servidor sin conexión');
          return;
        }
        void this.lanzarEscenario(esc, this.tituloVisible(e, esc));
      }
    });
  }

  @HostListener('document:keydown.escape')
  onEsc(): void {
    if (this.conflictOpen()) this.closeConflict('cancel');
    else if (this.dialogOpen()) this.closeDialog(false);
  }

  /** Pruebas visibles de un módulo, filtradas por el buscador. */
  pruebasDe(cat: { id: string; nombre?: string }): string[] {
    const ids = this.catalog.escenariosVisibles(cat as never);
    const q = this.filtro().trim().toLowerCase();
    if (!q) return ids;
    if (String(cat.nombre || '').toLowerCase().includes(q)) return ids;
    return ids.filter((id) => {
      const e = this.catalog.escenarios()[id];
      const t = this.tituloLista(id).toLowerCase();
      const desc = String(e?.descripcion || '').toLowerCase();
      const tag = String(e?.tag || '').toLowerCase();
      return t.includes(q) || desc.includes(q) || tag.includes(q) || id.toLowerCase().includes(q);
    });
  }

  /** Con búsqueda activa los módulos con resultados se muestran expandidos. */
  moduloAbierto(id: string): boolean {
    if (this.filtro().trim()) return true;
    return this.modulosAbiertos().includes(id);
  }

  private abrirModulo(id: string | null | undefined): void {
    if (!id) return;
    this.modulosAbiertos.update((a) => (a.includes(id) ? a : [...a, id]));
  }

  esModuloActivo(catId: string): boolean {
    return this.catalog.resolveModuloId(catId) === this.catalog.categoriaId();
  }

  esPruebaActiva(catId: string, escId: string): boolean {
    return this.esModuloActivo(catId) && escId === this.catalog.escenarioId();
  }

  private aplicarSeleccionModulo(catId: string, escId?: string | null, soloSiCambio = false): void {
    const resuelto = this.catalog.resolveModuloId(catId);
    if (soloSiCambio && resuelto === this.catalog.categoriaId()) {
      if (!escId || escId === this.catalog.escenarioId()) return;
    }
    if (!this.catalog.activarModulo(catId, escId ?? undefined)) {
      this.toast.warn(`Módulo no encontrado: ${catId}`);
      return;
    }
    this.modulosAbiertos.set([this.catalog.categoriaId()]);
    if (!soloSiCambio) {
      this.sincronizarQueryParams();
    }
    if (this.catalog.categoriaId() === 'release-9') {
      void this.cargarRelease9Info();
    }
  }

  private async cargarRelease9Info(): Promise<void> {
    try {
      const info = await firstValueFrom(this.api.getRelease9Info());
      this.release9Info.set(info?.ok ? info : null);
    } catch {
      this.release9Info.set(null);
    }
  }

  private sincronizarQueryParams(): void {
    this.sincronizandoQueryLocal = true;
    void this.router
      .navigate([], {
        relativeTo: this.route,
        queryParams: {
          cat: this.catalog.categoriaId(),
          esc: this.catalog.escenarioId() || null
        },
        queryParamsHandling: 'merge',
        replaceUrl: true
      })
      .finally(() => {
        window.setTimeout(() => (this.sincronizandoQueryLocal = false), 50);
      });
  }

  toggleModulo(id: string): void {
    if (this.filtro().trim()) this.filtro.set('');
    this.modulosAbiertos.update((abiertos) =>
      abiertos.includes(id) ? abiertos.filter((x) => x !== id) : [...abiertos, id]
    );
  }

  /** Selecciona módulo (cierra otros), primera prueba visible y actualiza detalle. */
  seleccionarModulo(catId: string): void {
    if (this.filtro().trim()) this.filtro.set('');
    this.aplicarSeleccionModulo(catId);
  }

  elegirPrueba(catId: string, escId: string): void {
    if (this.filtro().trim()) this.filtro.set('');
    this.aplicarSeleccionModulo(catId, escId);
  }

  /** Título legible del caso (corto), sin prefijo numérico ni script. */
  tituloVisible(
    esc: { corto?: string; titulo?: string; num?: string; generada?: boolean } | null | undefined,
    id?: string
  ): string {
    if (!esc) return id || '';
    const corto = String(esc.corto || '').trim();
    if (corto) return corto;
    if (esc.generada) {
      const t = String(esc.titulo || '').trim();
      if (t) return t;
      return 'Prueba ' + (esc.num || id || '');
    }
    const titulo = String(esc.titulo || '').trim();
    if (titulo) return titulo;
    return id || esc.num || '';
  }

  /** Título corto del caso (detalle de pantalla). */
  tituloCaso(
    esc: { corto?: string; titulo?: string; num?: string; generada?: boolean } | null | undefined,
    id?: string
  ): string {
    return this.tituloVisible(esc, id);
  }

  /** Detalle / contexto: qué se prueba en el escenario. */
  detalleCaso(esc: { corto?: string; descripcion?: string; titulo?: string } | null | undefined): string {
    if (!esc) return '';
    const desc = String(esc.descripcion || '').trim();
    const corto = String(esc.corto || '').trim();
    if (desc && desc !== corto) return desc;
    const titulo = String(esc.titulo || '').trim();
    if (titulo && titulo !== corto) return titulo;
    return '';
  }

  /** Título principal en detalle: corto + descripción en un solo bloque (confirmaciones). */
  tituloPrincipal(
    esc: { corto?: string; descripcion?: string; titulo?: string; num?: string; generada?: boolean } | null | undefined,
    id?: string
  ): string {
    if (!esc) return id || '';
    const corto = String(esc.corto || '').trim();
    const desc = String(esc.descripcion || '').trim();
    if (corto && desc && desc !== corto) {
      if (corto.endsWith('.') || corto.endsWith('!') || corto.endsWith('?')) {
        return `${corto} ${desc}`;
      }
      return `${corto}. ${desc}`;
    }
    return corto || desc || this.tituloVisible(esc, id);
  }

  esRegresionModuloEscenario(
    esc: { regresionModulo?: boolean } | null | undefined,
    id?: string | null
  ): boolean {
    return !!(esc?.regresionModulo || (id && id.endsWith('-regresion')));
  }

  /** Por defecto: Excel QA al terminar cada corrida individual (excepto lotes/agregadores). */
  esEscenarioSc476(id: string | null | undefined): boolean {
    return id === 'r9-SC-476' || id === 'r9-SC-476-qa';
  }

  escenarioEntregaQaExcel(
    esc: { entregaQaExcel?: boolean; regresionModulo?: boolean } | null | undefined,
    id: string | null,
    catId: string | null
  ): boolean {
    if (esc?.entregaQaExcel === false) return false;
    if (esc?.entregaQaExcel === true) return true;
    if (!id || !esc) return false;
    if (esc.regresionModulo || id.endsWith('-regresion')) return false;
    if (id === 'r9-todo' || id.endsWith('-todo') || id === 'regresion-pruebas-todo' || id === '00') {
      return false;
    }
    if (id === 'smoke-pipeline' || catId === 'cat-diagnostico') return false;
    if (catId === 'cat-regresion-pruebas') return false;
    return true;
  }

  private resolverModuloParaEscenario(escenarioId: string): Categoria | undefined {
    const cur = this.cat();
    if (cur?.escenarios?.includes(escenarioId)) return cur;
    return this.catalog.categorias().find((c) => (c.escenarios || []).includes(escenarioId));
  }

  cortoLista(id: string): string {
    return this.tituloVisible(this.catalog.escenarios()[id], id);
  }

  tituloLista(id: string): string {
    return this.tituloVisible(this.catalog.escenarios()[id], id);
  }

  ultimaCorridaTexto(id: string): string | null {
    return this.runs.ultimaCorridaTexto(id);
  }

  badge(id: string): 'ok' | 'fail' | 'none' {
    return this.runs.estadoEscenario(id);
  }

  private avisarYaEncolado(): void {
    this.toast.warn('Esa prueba ya está en ejecución o en cola');
  }

  private pedirAccionConflicto(tituloActual: string, tituloNueva: string): Promise<RunConflictAction> {
    this.conflictTituloActual.set(tituloActual);
    this.conflictTituloNueva.set(tituloNueva);
    this.conflictOpen.set(true);
    return new Promise((resolve) => {
      this.conflictResolver = resolve;
    });
  }

  closeConflict(action: RunConflictAction): void {
    this.conflictOpen.set(false);
    const r = this.conflictResolver;
    this.conflictResolver = null;
    if (r) r(action);
  }

  private tituloCorridaActual(): string {
    return this.runs.faseTitulo() || this.runs.ultima()?.titulo || 'Corrida activa';
  }

  /** Ejecuta o resuelve conflicto (cola / detener) si hay corrida activa. */
  async lanzarEscenario(
    escenarioId: string,
    titulo: string,
    opts?: { entregaQa?: boolean }
  ): Promise<void> {
    const esc = this.catalog.escenarios()[escenarioId];
    const entregaQa =
      opts?.entregaQa ??
      this.escenarioEntregaQaExcel(esc, escenarioId, this.catalog.categoriaId());

    if (this.runs.estaPendienteOEnCurso(escenarioId)) {
      this.avisarYaEncolado();
      return;
    }
    if (this.runs.enCurso()) {
      const action = await this.pedirAccionConflicto(this.tituloCorridaActual(), titulo);
      if (action === 'cancel') return;
      if (action === 'enqueue') {
        this.runs.encolar(escenarioId, titulo);
        return;
      }
      if (action === 'stop-and-run') {
        this.mostrarInformeEmbed.set(false);
        await this.runs.detenerYEjecutar(escenarioId, titulo, { entregaQa });
        return;
      }
    }
    this.mostrarInformeEmbed.set(false);
    await this.runs.ejecutar(escenarioId, titulo, { entregaQa });
  }

  private async encolarLote(ids: string[]): Promise<void> {
    const items = ids
      .map((id) => {
        const esc = this.catalog.escenarios()[id];
        if (!esc) return null;
        return { escenarioId: id, titulo: this.tituloVisible(esc, id) };
      })
      .filter((x): x is { escenarioId: string; titulo: string } => !!x);
    const nuevos = items.filter((it) => !this.runs.estaPendienteOEnCurso(it.escenarioId));
    if (!nuevos.length) {
      this.avisarYaEncolado();
      return;
    }
    const n = this.runs.encolarVarios(nuevos);
    this.toast.info(n ? `${n} prueba(s) agregadas a la cola` : 'Nada nuevo para encolar');
  }

  async confirmarEjecutar(): Promise<void> {
    const esc = this.esc();
    const id = this.catalog.escenarioId();
    if (!esc || !id) {
      this.statusMsg.set('No hay escenario seleccionado.');
      this.statusTipo.set('warn');
      this.toast.warn('Seleccioná una prueba');
      return;
    }
    if (!this.api.online()) {
      this.statusMsg.set('Servidor no disponible. Abrí Iniciar-Runner.bat');
      this.statusTipo.set('warn');
      this.toast.warn('Servidor sin conexión');
      return;
    }
    if (this.runs.enCurso()) {
      const esc = this.esc();
      const titulo = esc && id ? this.tituloVisible(esc, id) : 'Nueva prueba';
      const action = await this.pedirAccionConflicto(this.tituloCorridaActual(), titulo);
      if (action === 'cancel') return;
      if (action === 'enqueue') {
        if (this.esRegresionModuloEscenario(esc!, id!)) {
          await this.encolarLote(this.idsModuloEjecutables());
        } else if (id) {
          this.runs.encolar(id, titulo);
        }
        return;
      }
      if (action === 'stop-and-run') {
        if (this.esRegresionModuloEscenario(esc!, id!)) {
          await this.runs.detener();
          const ok = await this.runs.esperarFinCorrida();
          if (!ok) {
            this.toast.warn('No se pudo detener la corrida a tiempo');
            return;
          }
          await this.ejecutarTodosDelModulo();
        } else if (id) {
          await this.runs.detener();
          const okStop = await this.runs.esperarFinCorrida();
          if (!okStop) {
            this.toast.warn('No se pudo detener la corrida a tiempo');
            return;
          }
          await this.lanzarEscenario(id, titulo, {
            entregaQa: this.escenarioEntregaQaExcel(esc!, id!, this.catalog.categoriaId())
          });
        }
        return;
      }
    }
    if (this.esRegresionModuloEscenario(esc, id)) {
      await this.ejecutarTodosDelModulo();
      return;
    }
    const entregaQa = this.escenarioEntregaQaExcel(esc, id, this.catalog.categoriaId());
    const ok = await this.askConfirm(
      'Ejecutar corrida',
      this.tituloCaso(esc, id) +
        (this.detalleCaso(esc) ? '\n\n' + this.detalleCaso(esc) : '') +
        (entregaQa
          ? '\n\nAl finalizar se genera Excel QA (casos + PNG embebidos + queries COBIS) y ZIP de entrega.'
          : '\n\nAl finalizar se descarga el ZIP de evidencias automáticamente.'),
      'Ejecutar'
    );
    if (!ok) return;
    this.mostrarInformeEmbed.set(false);
    await this.lanzarEscenario(id, this.tituloVisible(esc, id), { entregaQa });
  }

  /** Excel + ZIP QA de un solo caso tras la corrida (PNG embebidos + queries si hay). */
  private async generarEntregaQaDesdeCorrida(
    escenarioId: string,
    titulo: string,
    r: RunResponse
  ): Promise<void> {
    const esc = this.catalog.escenarios()[escenarioId];
    const cat = this.resolverModuloParaEscenario(escenarioId);
    if (!esc || !cat) return;
    if (!this.escenarioEntregaQaExcel(esc, escenarioId, cat.id)) return;

    const pass = r.ok === true && !r.corridaVacia && !r.errorProceso;
    const casoId = this.resolverIdCaso(escenarioId, esc);
    const tituloCaso = this.armarTituloCasoExcel(esc);
    const casos = [this.armarFilaExcel(escenarioId, esc, r, pass)];
    const corridas = [
      {
        casoId,
        titulo: tituloCaso,
        tag: esc.tag,
        escenarioId,
        runId: r.runId
      }
    ];

    this.statusMsg.set('Generando Excel y ZIP QA…');
    this.statusTipo.set('info');
    await this.generarYDescargarPaqueteModulo(
      cat.nombre,
      casos,
      corridas,
      cat.id,
      casoId,
      escenarioId
    );
    this.statusMsg.set(
      pass ? `${casoId} OK — Excel QA listo` : `${casoId} FAIL — Excel QA con observaciones`
    );
    this.statusTipo.set(pass ? 'ok' : 'warn');
    if (!pass) this.toast.warn('Corrida con fallas; Excel QA generado igualmente');
  }

  /** Comprobación rápida: smoke-pipeline (build + list-tests). */
  async probarPipelineRunner(): Promise<void> {
    if (!this.api.online()) {
      this.toast.warn('Servidor sin conexión');
      return;
    }
    const ok = await this.askConfirm(
      'Comprobar instalación',
      'Compila el proyecto y lista pruebas automáticas (sin abrir SOT ni Chrome).\n\nÚtil la primera vez en tu PC o antes de lotes grandes.',
      'Comprobar'
    );
    if (!ok) return;
    this.aplicarSeleccionModulo('cat-diagnostico', 'smoke-pipeline');
    const e = this.catalog.escenarios()['smoke-pipeline'];
    if (!e) {
      this.toast.warn('Escenario smoke-pipeline no encontrado en catálogo');
      return;
    }
    await this.lanzarEscenario('smoke-pipeline', this.tituloVisible(e, 'smoke-pipeline'));
  }

  liberarCorridaLocal(): void {
    this.runs.liberarCorridaLocal();
    this.toast.info('Estado de corrida liberado');
  }

  async ejecutarTodosDelModulo(): Promise<void> {
    const ids = this.idsModuloEjecutables();
    const cat = this.cat();
    if (!ids.length || !cat) {
      this.toast.warn('Este módulo no tiene pruebas para ejecutar');
      return;
    }
    if (!this.api.online()) {
      this.toast.warn('Servidor sin conexión');
      return;
    }
    if (this.runs.enCurso()) {
      const action = await this.pedirAccionConflicto(
        this.tituloCorridaActual(),
        `Regresión del módulo (${ids.length})`
      );
      if (action === 'cancel') return;
      if (action === 'enqueue') {
        await this.encolarLote(ids);
        return;
      }
      if (action === 'stop-and-run') {
        await this.runs.detener();
        const okStop = await this.runs.esperarFinCorrida();
        if (!okStop) {
          this.toast.warn('No se pudo detener la corrida a tiempo');
          return;
        }
      }
    }

    const lista = ids.map((id) => this.tituloLista(id)).join('\n• ');
    const esR9 = this.catalog.categoriaId() === 'release-9';
    const ok = await this.askConfirm(
      esR9 ? 'Correr todo Release 9' : 'Regresión del módulo',
      esR9
        ? `${cat.nombre}\n\nSe ejecutará la suite automatizada del módulo:\n• ${lista}\n\nT.O. y TimeOut se prueban manualmente fuera del Runner.\n\nAl terminar se genera ZIP QA con Excel:\n• casos-de-prueba/{ticket}-Casos-y-Evidencias.xlsx (hojas Casos + Evidencias + Queries)`
        : `${cat.nombre}\n\nRegresión del módulo: ${ids.length} prueba(s):\n• ${lista}\n\nAl terminar se genera un ZIP QA con el Excel de casos ejecutados:\n• casos-de-prueba/{ticket}-Casos-y-Evidencias.xlsx (hojas Casos + Evidencias + Queries)`,
      esR9 ? 'Correr todo Release 9' : 'Regresión del módulo'
    );
    if (!ok) return;

    this.mostrarInformeEmbed.set(false);
    this.limpiarExcelModulo();
    this.runs.loteCancelado.set(false);
    this.runs.setBloquearColaAuto(true);
    this.statusMsg.set(`Módulo: ejecutando 0/${ids.length}…`);
    this.statusTipo.set('info');

    const casos: Array<{
      id: string;
      titulo: string;
      tag?: string;
      precondiciones: string;
      pasos: string;
      resultadoEsperado: string;
      resultadoObtenido: string;
      estado: string;
      observaciones?: string;
    }> = [];
    const corridas: Array<{
      casoId: string;
      titulo: string;
      tag?: string;
      escenarioId: string;
      runId?: string;
      evidenciaCarpeta?: string;
    }> = [];

    let fallos = 0;
    const resumenEstados: string[] = [];
    try {
    for (let i = 0; i < ids.length; i++) {
      if (this.runs.loteCancelado()) {
        this.statusMsg.set(`Lote detenido en ${i}/${ids.length}.`);
        this.statusTipo.set('warn');
        this.toast.warn('Pruebas detenidas');
        break;
      }
      const id = ids[i];
      const esc = this.catalog.escenarios()[id];
      if (!esc) continue;
      this.catalog.activarModulo(this.catalog.categoriaId(), id);
      const progreso = `Módulo ${i + 1}/${ids.length}: ${this.tituloVisible(esc, id)}`;
      this.statusMsg.set(progreso);
      const casoId = this.resolverIdCaso(id, esc);
      const tituloCaso = this.armarTituloCasoExcel(esc);
      try {
        const r = await this.runs.ejecutarYEsperar(id, tituloCaso, {
          descargarZip: false,
          progreso
        });
        const pass = r.ok === true && !r.corridaVacia && !r.errorProceso;
        if (!pass) fallos++;
        resumenEstados.push(`${pass ? 'OK' : 'FAIL'} ${casoId}`);
        casos.push(this.armarFilaExcel(id, esc, r, pass));
        corridas.push({
          casoId,
          titulo: tituloCaso,
          tag: esc.tag,
          escenarioId: id,
          runId: r.runId,
          evidenciaCarpeta: r.evidenciaCarpeta
        });
        if (this.runs.loteCancelado() || esCorridaDetenida(r)) {
          this.statusMsg.set(`Lote detenido tras ${i + 1}/${ids.length}.`);
          this.statusTipo.set('warn');
          break;
        }
      } catch (err: any) {
        fallos++;
        const msg = String(err?.error?.error || err?.message || err || 'Error al ejecutar');
        resumenEstados.push(`FAIL ${casoId}`);
        casos.push(this.armarFilaExcel(id, esc, { ok: false, error: msg, log: msg } as any, false));
        corridas.push({
          casoId,
          titulo: tituloCaso,
          tag: esc.tag,
          escenarioId: id
        });
        if (this.runs.loteCancelado() || /detenid|lote detenido/i.test(msg)) {
          this.statusMsg.set(`Lote detenido tras ${i + 1}/${ids.length}.`);
          this.statusTipo.set('warn');
          break;
        }
      }
    }
    } finally {
      this.runs.setBloquearColaAuto(false);
      this.runs.reanudarCola();
    }

    if (!casos.length) {
      this.statusMsg.set('No hay casos para empaquetar.');
      this.statusTipo.set('warn');
      return;
    }

    try {
      this.statusMsg.set('Generando Excel y ZIP QA…');
      await this.generarYDescargarPaqueteModulo(cat.nombre, casos, corridas, cat.id);
      const detalle = resumenEstados.join(' · ');
      this.statusMsg.set(
        fallos
          ? `Módulo: ${casos.length - fallos}/${casos.length} OK. ${detalle}`
          : `Módulo OK (${casos.length}). ${detalle}`
      );
      this.statusTipo.set(fallos ? 'warn' : 'ok');
      this.toast.ok(fallos ? 'ZIP QA listo (con fallas)' : 'ZIP QA de entrega listo');
    } catch (e: any) {
      this.statusMsg.set(e?.message || 'No se pudo generar el ZIP QA');
      this.statusTipo.set('error');
      this.toast.error('Falló la generación del ZIP QA');
    }
  }

  /** ID de caso para Excel/ZIP (p. ej. SC437-CA-01, CC-CA-01). */
  private resolverIdCaso(
    id: string,
    esc: { num?: string; titulo: string; tag?: string }
  ): string {
    const titulo = esc.titulo || '';
    const mCc = titulo.match(/\bCC\s+(CA|CN)-?0?(\d+)\b/i);
    if (mCc) {
      return `CC-${mCc[1].toUpperCase()}-${mCc[2].padStart(2, '0')}`;
    }
    const tag = String(esc.tag || '');
    const mCcTag = tag.match(/@CierreCuadre(CA|CN)0?(\d+)/i);
    if (mCcTag) {
      return `CC-${mCcTag[1].toUpperCase()}-${mCcTag[2].padStart(2, '0')}`;
    }
    if (id === '30' || id === '31') return `CC-${id}`;
    const mEscCc = id.match(/^31-(CA|CN)0?(\d+)$/i);
    if (mEscCc) {
      return `CC-${mEscCc[1].toUpperCase()}-${mEscCc[2].padStart(2, '0')}`;
    }
    const mCa = titulo.match(/\b(SC-?\d+)\s+(CA|CN)-?0?(\d+)\b/i);
    if (mCa) {
      const tk = mCa[1].replace(/-/g, '').toUpperCase();
      return `${tk}-${mCa[2].toUpperCase()}-${mCa[3].padStart(2, '0')}`;
    }
    const mTag = tag.match(/@(SC\d+)_(CA|CN)_(\d+)/i);
    if (mTag) {
      return `${mTag[1].toUpperCase()}-${mTag[2].toUpperCase()}-${mTag[3].padStart(2, '0')}`;
    }
    const mEsc = id.match(/^(\d+)-(CA|CN)0?(\d+)$/i);
    if (mEsc) {
      const tkTitulo = titulo.match(/\b(SC-?\d+)\b/i);
      if (tkTitulo) {
        const tk = tkTitulo[1].replace(/-/g, '').toUpperCase();
        return `${tk}-${mEsc[2].toUpperCase()}-${mEsc[3].padStart(2, '0')}`;
      }
    }
    return esc.num || id;
  }

  private armarFilaExcel(
    id: string,
    esc: { num?: string; titulo: string; descripcion?: string; corto?: string; tag?: string; prerequisitos?: string[]; resultado?: string; evidencias?: string[] },
    r: RunResponse,
    pass: boolean
  ) {
    const esperado = (esc.resultado || 'Cumplir el resultado del escenario').trim();
    const obtenido = pass
      ? esperado
      : truncarObs(
          resultadoObtenidoCorrida(r, pass, 'No cumple el resultado esperado.'),
          500
        );
    const obsPartes: string[] = [];
    if (!pass) {
      const msg = mensajeCorrida(r);
      if (msg) obsPartes.push(msg);
      const hint = extraerObservacionDeLog(r.log || '');
      if (hint && hint !== msg) obsPartes.push(hint);
    }

    return {
      id: this.resolverIdCaso(id, esc),
      titulo: this.armarTituloCasoExcel(esc),
      tag: esc.tag,
      precondiciones: (esc.prerequisitos || []).join('\n'),
      pasos: this.armarPasosCasoExcel(esc),
      resultadoEsperado: esperado,
      resultadoObtenido: obtenido,
      estado: pass ? 'PASS' : 'FAIL',
      observaciones: !pass && obsPartes.length ? obsPartes.join('\n') : undefined
    };
  }

  /** Título del caso en Excel (formato SC-514 / casos.json). */
  private armarTituloCasoExcel(esc: { titulo: string; corto?: string }): string {
    const corto = (esc.corto || '').trim();
    const titulo = (esc.titulo || '').trim();
    return corto || titulo;
  }

  /** Pasos del caso ejecutado (columna Pasos del Excel). */
  private armarPasosCasoExcel(esc: { titulo: string; descripcion?: string; corto?: string }): string {
    const desc = (esc.descripcion || '').trim();
    if (desc) return desc;
    const corto = (esc.corto || '').trim();
    if (corto) return corto;
    return esc.titulo || '';
  }

  private resolverTicketModulo(
    moduloId?: string,
    nombreModulo?: string,
    escenarioId?: string
  ): string | undefined {
    if (escenarioId) {
      const mR9 = escenarioId.match(/^r9-SC-(\d+)$/i);
      if (mR9) return `SC-${mR9[1]}`;
    }
    if (moduloId === 'cierre-cuadre' || moduloId === 'cierre-cuadre-131') return 'CC';
    if (moduloId === 'release-9' && escenarioId) {
      const m = escenarioId.match(/^r9-SC-(\d+)$/i);
      if (m) return `SC-${m[1]}`;
    }
    const ticketMatch = (nombreModulo || '').match(/\b(SC-?\d+)\b/i);
    return ticketMatch?.[1];
  }

  private async generarYDescargarPaqueteModulo(
    nombreModulo: string,
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
    }>,
    corridas: Array<{
      casoId: string;
      titulo: string;
      tag?: string;
      escenarioId: string;
      runId?: string;
      evidenciaCarpeta?: string;
    }>,
    moduloId?: string,
    ticketOverride?: string,
    escenarioId?: string
  ): Promise<void> {
    const ticket =
      ticketOverride ??
      this.resolverTicketModulo(moduloId, nombreModulo, escenarioId ?? corridas[0]?.escenarioId);
    const body = { nombreModulo, ticket, casos, corridas };

    const meta = await firstValueFrom(this.api.prepararPaqueteModulo(body));
    if (!meta?.ok || !meta.token) {
      throw new Error(meta?.error || 'No se pudo preparar el paquete QA');
    }

    const [excelResp, zipResp] = await Promise.all([
      firstValueFrom(this.api.descargarPaqueteModulo(meta.token, 'excel')),
      firstValueFrom(this.api.descargarPaqueteModulo(meta.token, 'zip'))
    ]);

    const excelNombre = this.nombreDesdeRespuesta(
      excelResp,
      'content-disposition',
      () =>
        meta.excelNombre ||
        `${(ticket || nombreModulo).replace(/\s+/g, '')}-Casos-y-Evidencias.xlsx`,
      true
    );
    const zipNombre = this.nombreDesdeRespuesta(
      zipResp,
      'content-disposition',
      () => meta.zipNombre || `${nombreModulo.replace(/\s+/g, '_')}-Evidencias.zip`,
      true
    );

    const excelBlob = await this.validarBlobDescarga(excelResp, 'Excel');
    const zipBlob = await this.validarBlobDescarga(zipResp, 'ZIP');

    this.registrarBlobModulo(this.excelModuloUrl, this.excelModuloBlob, excelBlob);
    this.excelModuloNombre.set(excelNombre);
    this.registrarBlobModulo(this.zipModuloUrl, this.zipModuloBlob, zipBlob);
    this.zipModuloNombre.set(zipNombre);

    const pasos = meta.pasosEvidencia ?? 0;
    await this.descargarModuloQaAutomatico(excelBlob, excelNombre, zipBlob, zipNombre, pasos);
  }

  /** Excel y ZIP QA: descarga programática (evita fallas de &lt;a download&gt; con blob). */
  private async descargarModuloQaAutomatico(
    excelBlob: Blob,
    excelNombre: string,
    zipBlob: Blob,
    zipNombre: string,
    pasosEvidencia: number
  ): Promise<void> {
    try {
      this.runs.descargarArchivo(excelBlob, excelNombre);
      await new Promise((r) => setTimeout(r, 900));
      this.runs.descargarArchivo(zipBlob, zipNombre);
      this.toast.ok(
        pasosEvidencia > 0
          ? `Descargados Excel y ZIP QA (${pasosEvidencia} capturas en Excel).`
          : 'Descargados Excel y ZIP QA.'
      );
    } catch (e: any) {
      this.toast.warn(
        `Archivos listos en pantalla; la descarga automática falló: ${e?.message || e}. Usá los botones.`
      );
    }
  }

  descargarExcelModuloManual(): void {
    const blob = this.excelModuloBlob();
    const nombre = this.excelModuloNombre();
    if (!blob || !nombre) {
      this.toast.warn('No hay Excel QA generado para este módulo.');
      return;
    }
    this.runs.descargarArchivo(blob, nombre);
  }

  descargarZipModuloManual(): void {
    const blob = this.zipModuloBlob();
    const nombre = this.zipModuloNombre();
    if (!blob || !nombre) {
      this.toast.warn('No hay ZIP QA generado para este módulo.');
      return;
    }
    this.runs.descargarArchivo(blob, nombre);
  }

  /** SC-476: Excel + ZIP con los 12 casos Xray desde casos.json (sin re-ejecutar Gherkin). */
  async descargarEntregaQaDesdeCasosJson(ticket = 'SC-476'): Promise<void> {
    if (!this.api.online()) {
      this.toast.warn('Servidor sin conexión');
      return;
    }
    try {
      this.statusMsg.set('Armando Excel QA desde casos.json…');
      this.statusTipo.set('info');
      const meta = await firstValueFrom(this.api.prepararPaqueteDesdeCasosJson(ticket));
      if (!meta?.ok || !meta.token) {
        throw new Error(meta?.error || 'No se pudo preparar el paquete QA');
      }
      const [excelResp, zipResp] = await Promise.all([
        firstValueFrom(this.api.descargarPaqueteModulo(meta.token, 'excel')),
        firstValueFrom(this.api.descargarPaqueteModulo(meta.token, 'zip'))
      ]);
      const excelNombre =
        meta.excelNombre || `${ticket.replace(/\s+/g, '')}-Casos-y-Evidencias.xlsx`;
      const zipNombre = meta.zipNombre || `${ticket}-Evidencias.zip`;
      const excelBlob = await this.validarBlobDescarga(excelResp, 'Excel');
      const zipBlob = await this.validarBlobDescarga(zipResp, 'ZIP');
      this.registrarBlobModulo(this.excelModuloUrl, this.excelModuloBlob, excelBlob);
      this.excelModuloNombre.set(excelNombre);
      this.registrarBlobModulo(this.zipModuloUrl, this.zipModuloBlob, zipBlob);
      this.zipModuloNombre.set(zipNombre);
      const pasos = meta.pasosEvidencia ?? 0;
      const nCasos = meta.casos ?? 0;
      await this.descargarModuloQaAutomatico(excelBlob, excelNombre, zipBlob, zipNombre, pasos);
      this.statusMsg.set(
        `Entrega QA ${ticket}: ${nCasos} casos, ${pasos} capturas embebidas.`
      );
      this.statusTipo.set('ok');
    } catch (e: any) {
      this.statusMsg.set(e?.message || 'No se pudo armar la entrega QA');
      this.statusTipo.set('error');
      this.toast.error('Falló la entrega QA desde casos.json');
    }
  }

  private registrarBlobModulo(
    urlSignal: { (): string | null; set: (v: string | null) => void },
    blobSignal: { (): Blob | null; set: (v: Blob | null) => void },
    blob: Blob
  ): void {
    const prev = urlSignal();
    if (prev) URL.revokeObjectURL(prev);
    blobSignal.set(blob);
    urlSignal.set(URL.createObjectURL(blob));
  }

  private async validarBlobDescarga(
    resp: { status: number; body: Blob | null },
    etiqueta: string
  ): Promise<Blob> {
    const blob = resp.body;
    if (resp.status < 200 || resp.status >= 300) {
      throw new Error(await this.motivoErrorBlob(blob, `${etiqueta}: error HTTP ${resp.status}`));
    }
    if (!blob || blob.size === 0) {
      throw new Error(`El servidor devolvió un ${etiqueta} vacío.`);
    }
    if (await this.esRespuestaError(blob)) {
      throw new Error(await this.motivoErrorBlob(blob, `El servidor respondió un error en lugar del ${etiqueta}.`));
    }
    if (!this.esArchivoBinarioValido(blob, etiqueta)) {
      throw new Error(`El ${etiqueta} recibido no parece un archivo válido (${blob.size} bytes).`);
    }
    return blob;
  }

  private async esRespuestaError(blob: Blob): Promise<boolean> {
    const tipo = (blob.type || '').toLowerCase();
    if (tipo.includes('json') || tipo === 'text/plain' || tipo.startsWith('text/html')) return true;
    if (blob.size > 512) return false;
    try {
      const texto = (await blob.slice(0, 256).text()).trim();
      return texto.startsWith('{') || texto.startsWith('<');
    } catch {
      return false;
    }
  }

  private esArchivoBinarioValido(blob: Blob, etiqueta: string): boolean {
    if (blob.size < 4) return false;
    // ZIP y XLSX son ZIP por dentro (PK..)
    if (etiqueta === 'Excel' || etiqueta === 'ZIP') return true;
    const tipo = (blob.type || '').toLowerCase();
    return (
      tipo.includes('zip') ||
      tipo.includes('spreadsheet') ||
      tipo.includes('octet-stream') ||
      tipo === ''
    );
  }

  private nombreDesdeRespuesta(
    resp: { headers: { get(name: string): string | null } },
    header: string,
    fallback: () => string,
    desdeContentDisposition = false
  ): string {
    if (desdeContentDisposition) {
      const cd = resp.headers.get('content-disposition') || '';
      const m = /filename\*?=(?:UTF-8''|")?([^\";]+)/i.exec(cd);
      if (m?.[1]) {
        try {
          return decodeURIComponent(m[1].replace(/"/g, '').trim());
        } catch {
          return m[1].replace(/"/g, '').trim();
        }
      }
      return fallback();
    }
    return resp.headers.get(header)?.trim() || fallback();
  }

  private registrarBlobUrl(
    urlSignal: { (): string | null; set: (v: string | null) => void },
    blob: Blob
  ): string {
    const prev = urlSignal();
    if (prev) URL.revokeObjectURL(prev);
    const url = URL.createObjectURL(blob);
    urlSignal.set(url);
    return url;
  }

  private dispararDescargaArchivo(url: string, nombre: string): void {
    const a = document.createElement('a');
    a.href = url;
    a.download = nombre;
    a.rel = 'noopener';
    a.style.display = 'none';
    document.body.appendChild(a);
    a.click();
    a.remove();
  }

  private async motivoErrorBlob(blob: Blob | null, fallback: string): Promise<string> {
    if (!blob) return fallback;
    try {
      const texto = await blob.text();
      const json = JSON.parse(texto);
      return String(json?.error || texto).slice(0, 300);
    } catch {
      return fallback;
    }
  }

  private limpiarExcelModulo(): void {
    const prevZip = this.zipModuloUrl();
    if (prevZip) URL.revokeObjectURL(prevZip);
    const prevExcel = this.excelModuloUrl();
    if (prevExcel) URL.revokeObjectURL(prevExcel);
    this.zipModuloUrl.set(null);
    this.zipModuloNombre.set('');
    this.zipModuloBlob.set(null);
    this.excelModuloUrl.set(null);
    this.excelModuloNombre.set('');
    this.excelModuloBlob.set(null);
  }

  async detenerPruebas(): Promise<void> {
    await this.runs.detener();
    this.toast.warn('Deteniendo pruebas…');
  }

  quitarDeCola(colaId: string): void {
    this.runs.quitarDeCola(colaId);
    this.toast.info('Quitado de la cola');
  }

  vaciarColaPendiente(): void {
    this.runs.vaciarCola();
    this.toast.info('Cola vaciada');
  }

  async ejecutarRegresionCompleta(): Promise<void> {
    if (this.runs.enCurso()) {
      const action = await this.pedirAccionConflicto(
        this.tituloCorridaActual(),
        'Correr todo (regresión completa)'
      );
      if (action === 'cancel') return;
      if (action === 'enqueue') {
        this.toast.info('La regresión completa se lanza desde el módulo dedicado al confirmar.');
        return;
      }
      if (action === 'stop-and-run') {
        await this.runs.detener();
        const okStop = await this.runs.esperarFinCorrida();
        if (!okStop) {
          this.toast.warn('No se pudo detener la corrida a tiempo');
          return;
        }
      }
    }
    const n = this.catalog.countPruebasRegresion();
    const ok = await this.askConfirm(
      'Correr todo (regresión completa)',
      `Módulo aparte con el total de pruebas generadas (${n} fuera de Release 9).\nRelease 9 usa «Correr todo Release 9» en su módulo.`,
      'Correr todo'
    );
    if (!ok) return;
    await this.router.navigate(['/runner'], { queryParams: QUERY_REGRESION_COMPLETA });
  }

  askConfirm(title: string, body: string, confirmLabel: string): Promise<boolean> {
    this.dialogTitle.set(title);
    this.dialogBody.set(body);
    this.dialogConfirmLabel.set(confirmLabel);
    this.dialogOpen.set(true);
    return new Promise((resolve) => {
      this.dialogResolver = resolve;
    });
  }

  closeDialog(ok: boolean): void {
    this.dialogOpen.set(false);
    const r = this.dialogResolver;
    this.dialogResolver = null;
    if (r) r(ok);
  }

  async verInforme(): Promise<void> {
    const u = this.corridaEvidencia();
    if (!u || !this.tieneInforme()) {
      this.toast.warn('No hay informe HTML de esta corrida');
      return;
    }
    const err = await this.runs.abrirInforme(u, 'pestaña');
    if (err) this.toast.warn(err);
  }

  async irAlInformeEmbed(): Promise<void> {
    const u = this.corridaEvidencia();
    if (!u || !this.tieneInforme()) {
      this.toast.warn('No hay informe HTML de esta corrida');
      return;
    }
    const err = await this.runs.abrirInforme(u, 'blobUrl');
    if (err) {
      this.toast.warn(err);
      return;
    }
    const href = this.runs.informeBlobHref();
    this.informeBlobSafe.set(
      href ? this.sanitizer.bypassSecurityTrustResourceUrl(href) : null
    );
    this.mostrarInformeEmbed.set(true);
    queueMicrotask(() => {
      document.getElementById('evidenciaBox')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    });
  }

  async descargarZipManual(): Promise<void> {
    const u = this.corridaEvidencia();
    if (!u) return;
    const error = await this.runs.descargarZip(u, true);
    if (error) {
      this.statusMsg.set(`No se pudo descargar el ZIP: ${error}`);
      this.statusTipo.set('error');
      this.toast.error('Falló la descarga del ZIP');
    }
  }
}

function truncarObs(texto: string, max: number): string {
  const t = (texto || '').trim();
  if (t.length <= max) return t;
  return t.slice(0, max - 1) + '…';
}
