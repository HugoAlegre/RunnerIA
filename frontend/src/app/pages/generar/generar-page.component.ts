import { Component, HostListener, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { CatalogService, ModuloListaItem } from '../../core/services/catalog.service';
import { PruebasAdminService } from '../../core/services/pruebas-admin.service';
import { ToastService } from '../../core/services/toast.service';
import { WorkspaceProfileService } from '../../core/services/workspace-profile.service';
import { QUERY_REGRESION_COMPLETA } from '../../core/run-query';
import { ConfirmDialogComponent } from '../../shared/confirm-dialog/confirm-dialog.component';
import { RunnerIaPanelComponent } from './panels/runner-ia-panel.component';
import { CrearFeatureFormComponent } from './panels/crear-feature-form.component';
import { GherkinPreviewComponent } from './panels/gherkin-preview.component';
import { ModulosListaComponent } from './panels/modulos-lista.component';
import {
  MAX_ARCHIVOS,
  clasificarArchivosEntrada,
  contarFeaturesGherkin,
  detectarMojibake,
  esGherkinListo,
  extensionDeArchivo,
  extraerEscenarioFeature,
  extraerTituloFeature,
  leerArchivoComoTexto,
  RX_GHERKIN_FEATURE,
  RX_GHERKIN_SCENARIO,
  sugerirLanguageEs
} from './upload-archivos.util';
import {
  esArchivoMapeoUi,
  extraerGherkinDeMapeoJson,
  mensajeMapeoSinGherkin
} from './mapeo-json.util';

@Component({
  selector: 'app-generar-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterLink,
    ConfirmDialogComponent,
    RunnerIaPanelComponent,
    CrearFeatureFormComponent,
    GherkinPreviewComponent,
    ModulosListaComponent
  ],
  templateUrl: './generar-page.component.html',
  styleUrl: './generar-page.component.scss'
})
export class GenerarPageComponent implements OnInit {
  private api = inject(ApiService);
  private pruebasAdmin = inject(PruebasAdminService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  readonly catalog = inject(CatalogService);
  readonly toast = inject(ToastService);
  private readonly workspace = inject(WorkspaceProfileService);

  /** Tabs: crear | modulos */
  tab = signal<'crear' | 'modulos'>('crear');
  analizando = signal(false);
  faseAnalizar = signal<'idle' | 'jira' | 'mapa' | 'gherkin' | 'listo'>('idle');
  menuModuloId = signal<string | null>(null);

  status = signal('');
  statusTipo = signal('info');
  jiraUrl = '';
  texto = '';
  nombreFeature = '';
  detalle = '';
  featurePreview = '';
  grupoId = '__auto__';
  grupoNuevo = '';
  forzarIncompleto = false;
  /** Al importar lote: reemplazar .feature si ya existe. */
  sobrescribirLote = false;
  editandoArchivo = '';
  /** Módulo inferido del ticket/análisis cuando destino = automático. */
  moduloSugerido = '';
  casos = signal<any[]>([]);
  grupos = signal<any[]>([]);
  modulos = signal<ModuloListaItem[]>([]);
  abiertos = signal<Record<string, boolean>>({});
  renameId = signal<string | null>(null);
  renameValue = '';
  gherkinOpen = signal<Record<string, string>>({});
  deshacerHint = signal('');
  puedeDeshacer = signal(false);
  docs: File[] = [];
  readonly maxDocs = MAX_ARCHIVOS;
  /** Nombre del .feature cargado por drag-drop / archivo (solo UI). */
  gherkinOrigenArchivo = '';

  /** RunnerIA embebido (solapa colapsable). */
  runnerIaOpen = signal(false);
  riaGuardando = signal(false);
  riaStatus = signal('');
  riaStatusTipo = signal('info');
  mapaIaHint = signal('');
  proyectoNombre = '';
  proyectoActivoId = '';
  iaAlcanceTexto = '';
  proyectos: Array<{ id: string; nombre: string; descripcion?: string }> = [];
  checklist: Array<{ id: string; texto: string }> = [];
  proyectoFormModo = signal<'nuevo' | 'editar' | null>(null);
  proyectoFormNombre = '';
  proyectoFormDescripcion = '';
  proyectoFormId = '';
  proyectoFormIaNombre = 'RunnerIA';
  eliminarProyectoDialog = signal(false);

  /** Confirmación in-page (estilo sot-dialog). */
  dialogOpen = signal(false);
  dialogTitle = signal('');
  dialogBody = signal('');
  dialogConfirmLabel = signal('Confirmar');
  dialogDanger = signal(false);
  private dialogResolver: ((ok: boolean) => void) | null = null;

  get descripcionProyecto(): string {
    return this.proyectos.find((x) => x.id === this.proyectoActivoId)?.descripcion || '';
  }

  /** Hay borrador Gherkin: Guardar pasa a CTA primario. */
  get tienePreview(): boolean {
    return !!this.featurePreview.trim();
  }

  /** Mostrar importar lote visible cuando hay varios Feature o directivas # Modulo:. */
  get mostrarImportarLoteVisible(): boolean {
    const t = this.featurePreview.trim();
    if (!t) return false;
    if (contarFeaturesGherkin(t) > 1) return true;
    return /#\s*M[oó]dulo\s*:/i.test(t);
  }

  get detalleChars(): number {
    return this.detalle.trim().length;
  }

  @HostListener('document:keydown.escape')
  onEsc(): void {
    if (this.dialogOpen()) this.closeDialog(false);
    else if (this.menuModuloId()) this.menuModuloId.set(null);
  }

  @HostListener('document:click')
  onDocClick(): void {
    if (this.menuModuloId()) this.menuModuloId.set(null);
  }

  toggleMenuModulo(id: string, ev: Event): void {
    ev.stopPropagation();
    this.menuModuloId.update((cur) => (cur === id ? null : id));
  }

  async ngOnInit(): Promise<void> {
    this.cargarJiraUrlCompartida();
    await this.cargarRunnerIa();
    await this.refreshLista();
    this.consumirGherkinDesdeMapeo();
    this.route.queryParamMap.subscribe((q) => {
      const m = q.get('modulo');
      if (m) {
        this.prepararFormularioNuevaPrueba(m);
        this.tab.set('crear');
        this.abiertos.update((a) => ({ ...a, [m]: true }));
        this.status.set('Completá el detalle, analizá y guardá la nueva prueba.');
        this.statusTipo.set('info');
      }
      const editar = q.get('editar');
      if (editar) {
        this.tab.set('crear');
        void this.editarPrueba({
          id: editar,
          generada: true,
          pruebaArchivo: editar,
          titulo: editar
        });
      }
      const suite = q.get('suite');
      if (suite) {
        this.tab.set('crear');
        const e = this.catalog.escenarios()[suite];
        void this.editarPrueba({
          id: suite,
          generada: false,
          tag: e?.tag,
          titulo: e?.titulo || suite
        });
      }
    });
  }

  async refreshLista(): Promise<void> {
    try {
      const data = await firstValueFrom(this.api.getPruebasEscenarios());
      this.casos.set(data.casos || []);
      this.grupos.set(data.grupos || []);
      await this.catalog.syncPruebas();
      this.modulos.set(this.catalog.listaModulosCompleta());
      this.refreshDestinos();
      await this.refreshDeshacer();
    } catch (e: any) {
      this.modulos.set(this.catalog.listaModulosCompleta());
      const auth = e?.status === 401 || e?.error?.authRequired;
      this.status.set(
        auth
          ? 'Sesión expirada. Ingresá el PIN en el cuadro de acceso (servidor reiniciado).'
          : 'No se pudo cargar la lista (servidor offline).'
      );
      this.statusTipo.set('warn');
    }
  }

  private refreshDestinos(): void {
    const valid = new Set<string>(['__auto__', '__nuevo__', ...this.modulos().map((m) => m.id)]);
    if (!valid.has(this.grupoId)) this.grupoId = '__auto__';
  }

  private async refreshDeshacer(): Promise<void> {
    const d = await this.pruebasAdmin.refreshDeshacer();
    this.puedeDeshacer.set(d.disponible);
    this.deshacerHint.set(d.hint);
  }

  seleccionarModuloDestino(id: string): void {
    const m = this.modulos().find((x) => x.id === id);
    if (m) {
      this.grupoId = m.id;
      this.grupoNuevo = '';
      return;
    }
    this.grupoId = '__nuevo__';
    this.grupoNuevo = '';
  }

  /** Limpia el borrador y fija el módulo destino (Agregar desde «Módulos»). */
  private prepararFormularioNuevaPrueba(moduloId?: string): void {
    this.editandoArchivo = '';
    this.nombreFeature = '';
    this.detalle = '';
    this.texto = '';
    this.featurePreview = '';
    this.forzarIncompleto = false;
    this.moduloSugerido = '';
    this.faseAnalizar.set('idle');
    if (moduloId) this.seleccionarModuloDestino(moduloId);
  }

  private scrollAlFormularioGenerar(): void {
    setTimeout(() => {
      document.getElementById('formGenerar')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }, 0);
  }

  /** Todos los módulos visibles (suite fija + testing) para elegir destino. */
  get destinosModulos(): ModuloListaItem[] {
    return this.modulos();
  }

  get destinoLabel(): string {
    if (this.grupoId === '__auto__') {
      return this.moduloSugerido
        ? `Automático → ${this.moduloSugerido}`
        : 'Automático (se toma del ticket al Analizar)';
    }
    if (this.grupoId === '__nuevo__') return this.grupoNuevo.trim() || 'Módulo nuevo';
    const m = this.modulos().find((x) => x.id === this.grupoId);
    return m ? m.nombre + (m.fijo ? ' (suite)' : '') : this.grupoId;
  }

  private cargarJiraUrlCompartida(): void {
    try {
      const fromKey = (localStorage.getItem('sot-runner-pruebas-jira-url') || '').trim();
      if (fromKey) {
        this.jiraUrl = fromKey;
        return;
      }
      const j = JSON.parse(
        sessionStorage.getItem('sot-runner-pruebas-jira') ||
          localStorage.getItem('sot-runner-pruebas-jira-meta') ||
          'null'
      );
      if (j?.url) this.jiraUrl = String(j.url).trim();
    } catch {
      /* ignore */
    }
  }

  onJiraUrlChange(): void {
    try {
      const u = this.jiraUrl.trim();
      if (u) localStorage.setItem('sot-runner-pruebas-jira-url', u);
      else localStorage.removeItem('sot-runner-pruebas-jira-url');
      const sess = sessionStorage.getItem('sot-runner-pruebas-jira');
      if (sess) {
        const j = JSON.parse(sess);
        j.url = u;
        sessionStorage.setItem('sot-runner-pruebas-jira', JSON.stringify(j));
      }
      const meta = localStorage.getItem('sot-runner-pruebas-jira-meta');
      if (meta) {
        const j = JSON.parse(meta);
        j.url = u;
        localStorage.setItem('sot-runner-pruebas-jira-meta', JSON.stringify(j));
      }
    } catch {
      /* ignore */
    }
  }

  private jiraCreds(): { email: string; token: string } {
    try {
      const j = JSON.parse(
        sessionStorage.getItem('sot-runner-pruebas-jira') ||
          localStorage.getItem('sot-runner-pruebas-jira') ||
          'null'
      );
      return { email: j?.email || '', token: j?.token || '' };
    } catch {
      return { email: '', token: '' };
    }
  }

  private llmCfg(): { enabled?: boolean; apiKey?: string; model?: string; baseUrl?: string } | null {
    try {
      const l = JSON.parse(
        sessionStorage.getItem('sot-runner-pruebas-llm') ||
          localStorage.getItem('sot-runner-pruebas-llm') ||
          'null'
      );
      if (!l) return null;
      return {
        enabled: !!l.enabled,
        apiKey: l.apiKey || l.key || '',
        model: l.model || 'gpt-4o-mini',
        baseUrl: l.baseUrl || ''
      };
    } catch {
      return null;
    }
  }

  async analizar(): Promise<void> {
    if (!this.api.online()) {
      this.status.set('Servidor offline.');
      this.statusTipo.set('error');
      this.toast.error('Servidor offline');
      return;
    }
    if (!this.detalle.trim() && !this.jiraUrl.trim() && !this.docs.length) {
      this.status.set('Completá Detalle, un link Jira o documentación.');
      this.statusTipo.set('warn');
      this.toast.warn('Falta Detalle, Jira o docs');
      return;
    }
    const fd = new FormData();
    fd.append('tituloSugerido', this.nombreFeature || '');
    if (this.jiraUrl.trim()) {
      this.onJiraUrlChange();
    }
    fd.append('jiraUrl', this.jiraUrl || '');
    const creds = this.jiraCreds();
    if (creds.email) fd.append('jiraEmail', creds.email);
    if (creds.token) fd.append('jiraToken', creds.token);
    const textoManual = [this.detalle, this.texto].filter(Boolean).join('\n');
    fd.append('textoManual', textoManual);
    fd.append('texto', textoManual); // compat
    for (const f of this.docs) {
      if (f.name.toLowerCase().endsWith('.feature')) continue;
      fd.append('files', f, f.name);
    }
    const llm = this.llmCfg();
    if (llm?.apiKey) {
      if (llm.enabled) fd.append('usarLlm', 'true');
      fd.append('llmApiKey', llm.apiKey);
      fd.append('llmModel', llm.model || 'gpt-4o-mini');
      if (llm.baseUrl) fd.append('llmBaseUrl', llm.baseUrl);
      fd.append('interpretarCapturas', 'true');
    }
    if (this.grupoId && this.grupoId !== '__auto__') {
      fd.append('grupoId', this.grupoId);
      const nom =
        this.grupoId === '__nuevo__'
          ? this.grupoNuevo.trim() || this.moduloSugerido || ''
          : this.modulos().find((x) => x.id === this.grupoId)?.nombre || this.destinoLabel;
      if (nom) fd.append('grupoNombre', nom);
    }
    this.analizando.set(true);
    this.faseAnalizar.set(this.jiraUrl.trim() ? 'jira' : 'mapa');
    this.status.set(
      this.docs.length
        ? `Analizando (Jira/texto + ${this.docs.length} doc${this.docs.length === 1 ? '' : 's'})…`
        : 'Analizando…'
    );
    this.statusTipo.set('info');
    const phaseTimer = window.setTimeout(() => {
      if (this.analizando()) this.faseAnalizar.set('gherkin');
    }, 1200);
    try {
      const data = await firstValueFrom(this.api.analizarPruebas(fd));
      window.clearTimeout(phaseTimer);
      if (data?.ok === false) {
        this.status.set(data.error || 'No se pudo analizar.');
        this.statusTipo.set('error');
        this.toast.error(data.error || 'No se pudo analizar');
        this.faseAnalizar.set('idle');
        return;
      }
      this.faseAnalizar.set('listo');
      this.featurePreview = data.featureBorrador || data.feature || data.gherkin || data.contenido || '';

      if (!this.nombreFeature.trim()) {
        this.nombreFeature =
          (data.tituloTicket || data.tituloSugerido || this.extraerTituloFeature(this.featurePreview) || '').trim();
      }
      if (!this.detalle.trim()) {
        this.detalle = (
          data.detalleSugerido ||
          data.descripcionCaso ||
          data.resumen ||
          this.extraerDetalleCorto(this.featurePreview, this.nombreFeature) ||
          ''
        ).trim();
      }

      const sugeridoMod = String(data.moduloSugerido || '').trim();
      if (sugeridoMod) this.moduloSugerido = sugeridoMod;
      if (this.grupoId === '__auto__' || !this.grupoId) {
        this.aplicarModuloSugerido(sugeridoMod);
      } else if (sugeridoMod && this.grupoId === '__nuevo__' && !this.grupoNuevo.trim()) {
        this.grupoNuevo = sugeridoMod;
      }

      if (data.grupoId && this.grupoId !== '__auto__' && this.grupoId !== '__nuevo__') {
        /* respetar elección manual */
      }
      const avisos = Array.isArray(data.avisos)
        ? data.avisos
        : Array.isArray(data.warnings)
          ? data.warnings
          : [];
      const autoParts: string[] = [];
      if (this.nombreFeature.trim()) autoParts.push('título');
      if (this.detalle.trim()) autoParts.push('detalle');
      if (this.grupoId === '__auto__' || this.moduloSugerido) autoParts.push('módulo');
      const autoMsg =
        autoParts.length && (data.tituloTicket || data.detalleSugerido || data.descripcionCaso || data.resumen || sugeridoMod)
          ? ` Se completó ${autoParts.join(', ')} desde lo leído.`
          : '';
      const docsMsg = this.docs.length
        ? ` Se tuvieron en cuenta ${this.docs.length} archivo(s) de documentación.`
        : '';
      if (avisos.length) {
        this.status.set(`Borrador listo.${docsMsg}${autoMsg} Avisos: ${avisos.slice(0, 3).join(' · ')}`);
        this.statusTipo.set('warn');
        this.toast.warn('Borrador listo (con avisos)');
      } else {
        this.status.set(`Borrador listo. Revisá y guardá.${docsMsg}${autoMsg}`);
        this.statusTipo.set('ok');
        this.toast.ok('Borrador Gherkin listo');
      }
    } catch (e: any) {
      this.status.set(e?.error?.error || e?.message || 'Error al analizar');
      this.statusTipo.set('error');
      this.toast.error(e?.error?.error || e?.message || 'Error al analizar');
      this.faseAnalizar.set('idle');
    } finally {
      this.analizando.set(false);
      window.clearTimeout(phaseTimer);
    }
  }

  /** Elige un módulo existente por id/nombre, o deja «crear nuevo» con el nombre sugerido. */
  private aplicarModuloSugerido(sugerido: string): void {
    const s = (sugerido || '').trim();
    if (!s) {
      this.grupoId = '__auto__';
      return;
    }
    const all = this.modulos();
    const norm = (x: string) =>
      x
        .toLowerCase()
        .normalize('NFD')
        .replace(/[\u0300-\u036f]/g, '')
        .replace(/\s*\(opcional\)\s*$/i, '')
        .trim();
    const sn = norm(s);

    const byId = all.find((m) => norm(m.id) === sn);
    if (byId) {
      this.grupoId = byId.id;
      this.grupoNuevo = '';
      return;
    }
    const byName = all.find((m) => {
      const mn = norm(m.nombre);
      return mn === sn || mn.includes(sn) || sn.includes(mn);
    });
    if (byName) {
      this.grupoId = byName.id;
      this.grupoNuevo = '';
      return;
    }
    // No existe aún → preparar módulo nuevo con ese nombre
    this.grupoId = '__nuevo__';
    this.grupoNuevo = s;
  }

  /** Extrae el título desde Feature: o Característica: */
  private extraerTituloFeature(gherkin: string): string {
    return extraerTituloFeature(gherkin);
  }

  /** Arma un detalle corto si el API no mandó uno. */
  private extraerDetalleCorto(gherkin: string, titulo: string): string {
    const scen = extraerEscenarioFeature(gherkin);
    if (scen) return scen;
    if (titulo.trim()) return `Cubrir: ${titulo.trim()}`;
    return '';
  }

  onArchivosRecibidos(files: File[]): void {
    void this.procesarArchivosEntrada(files);
  }

  /** Gherkin enviado desde Mapeo UI (botón «Enviar a Generar»). */
  private consumirGherkinDesdeMapeo(): void {
    const gh = sessionStorage.getItem('runner.mapeoGherkin');
    if (!gh?.trim()) return;
    const nombre = sessionStorage.getItem('runner.mapeoGherkinNombre') || 'mapeo-ui-sesion.json';
    sessionStorage.removeItem('runner.mapeoGherkin');
    sessionStorage.removeItem('runner.mapeoGherkinNombre');
    this.featurePreview = gh.trim();
    this.gherkinOrigenArchivo = nombre;
    if (!this.nombreFeature.trim()) {
      this.nombreFeature = this.extraerTituloFeature(this.featurePreview);
    }
    this.tab.set('crear');
    this.status.set('Gherkin importado desde Mapeo UI. Revisá pasos y Guardá prueba.');
    this.statusTipo.set('ok');
    this.toast.ok('Mapeo UI → Generar');
  }

  async procesarArchivosEntrada(files: File[]): Promise<void> {
    if (!files.length) return;

    const cupo = Math.max(0, this.maxDocs - this.docs.length);
    const { gherkin, docs, rechazados, demasiadoGrandes, duplicados } = clasificarArchivosEntrada(
      files,
      this.docs,
      cupo
    );

    const msgs: string[] = [];

    if (docs.length) {
      const docsRestantes: File[] = [];
      for (const f of docs) {
        if (extensionDeArchivo(f.name) === '.json') {
          try {
            const txt = await leerArchivoComoTexto(f);
            if (esArchivoMapeoUi(txt)) {
              const gh = extraerGherkinDeMapeoJson(txt);
              if (gh) {
                this.featurePreview = gh;
                this.gherkinOrigenArchivo = f.name;
                if (!this.nombreFeature.trim()) {
                  this.nombreFeature = this.extraerTituloFeature(gh);
                }
                msgs.push(
                  `Gherkin importado desde Mapeo UI («${f.name}»). Revisá pasos y Guardá prueba.`
                );
                this.toast.ok('Mapeo UI → Gherkin');
                continue;
              }
              msgs.push(mensajeMapeoSinGherkin(f.name));
              continue;
            }
          } catch {
            /* tratar como doc normal */
          }
        }
        docsRestantes.push(f);
      }
      if (docsRestantes.length) {
        this.docs = [...this.docs, ...docsRestantes];
        msgs.push(`Documentación: +${docsRestantes.length} (total ${this.docs.length}).`);
      }
    }

    if (gherkin.length) {
      try {
        const textos: string[] = [];
        for (const f of gherkin) {
          textos.push(await leerArchivoComoTexto(f));
        }
        const combinado = textos.join('\n\n---\n\n').trim();
        if (!combinado) {
          msgs.push('El .feature está vacío.');
        } else if (!esGherkinListo(combinado)) {
          msgs.push(
            `«${gherkin.map((f) => f.name).join('», «')}» no parece Gherkin completo (Feature/Característica + Escenario/Scenario + pasos Dado/Cuando/Entonces). Revisá el archivo.`
          );
          this.featurePreview = combinado;
          this.gherkinOrigenArchivo = gherkin.map((f) => f.name).join(', ');
        } else {
          this.featurePreview = combinado;
          this.gherkinOrigenArchivo = gherkin.map((f) => f.name).join(', ');
          if (!this.nombreFeature.trim() && gherkin.length === 1) {
            this.nombreFeature = this.extraerTituloFeature(combinado);
          }
          msgs.push(
            gherkin.length > 1
              ? `Gherkin: ${gherkin.length} .feature cargados. Revisá y usá «Importar como lote» si corresponde.`
              : `Gherkin cargado desde «${gherkin[0].name}». Revisá y Guardá sin Analizar.`
          );
          this.toast.ok(gherkin.length > 1 ? 'Gherkin (lote) en vista previa' : 'Gherkin cargado');
        }

        if (detectarMojibake(combinado)) {
          msgs.push('Aviso: posible encoding roto (mojibake). Revisá «contraseña» y acentos.');
        }
        if (sugerirLanguageEs(combinado)) {
          msgs.push('Sugerencia: agregá «# language: es» al inicio del .feature.');
        }
      } catch (e: any) {
        msgs.push(e?.message || 'No se pudo leer el .feature.');
        this.statusTipo.set('error');
      }
    }

    if (demasiadoGrandes.length) {
      msgs.push(`Demasiado grande (máx. 15 MB): ${demasiadoGrandes.slice(0, 3).join(', ')}.`);
    }
    if (duplicados.length) {
      msgs.push(`Ya estaban: ${duplicados.slice(0, 3).join(', ')}.`);
    }
    if (rechazados.length) {
      msgs.push(
        rechazados
          .slice(0, 3)
          .map((r) => `«${r.nombre}»: ${r.motivo}`)
          .join(' ')
      );
    }

    if (msgs.length) {
      const hayError = rechazados.length > 0 || demasiadoGrandes.length > 0;
      const hayWarn = hayError || (gherkin.length > 0 && !esGherkinListo(this.featurePreview));
      this.status.set(msgs.join(' '));
      this.statusTipo.set(hayError ? 'error' : hayWarn ? 'warn' : 'ok');
      if (hayError) this.toast.warn(msgs[msgs.length - 1]);
    }
  }

  quitarDoc(i: number): void {
    this.docs = this.docs.filter((_, idx) => idx !== i);
  }

  limpiarDocs(): void {
    this.docs = [];
  }

  private nombreArchivo(): string {
    let base = (this.nombreFeature || 'Prueba').trim();
    base = base
      .normalize('NFD')
      .replace(/[\u0300-\u036f]/g, '')
      .replace(/[^A-Za-z0-9_\-]+/g, '_')
      .replace(/_+/g, '_')
      .replace(/^_|_$/g, '');
    if (!base || !/^[A-Za-z0-9]/.test(base)) {
      base = 'Prueba_' + new Date().toISOString().replace(/[-:TZ.]/g, '').slice(0, 14);
    }
    if (base.length > 80) base = base.slice(0, 80).replace(/_+$/g, '');
    if (!base.toLowerCase().endsWith('.feature')) base += '.feature';
    return base;
  }

  async guardar(): Promise<void> {
    if (!this.featurePreview.trim()) {
      this.status.set('Primero analizá para obtener el feature.');
      this.statusTipo.set('warn');
      return;
    }
    // Automático sin haber analizado módulo: intentar del ticket cacheado o crear nuevo
    if (this.grupoId === '__auto__') {
      if (this.moduloSugerido.trim()) this.aplicarModuloSugerido(this.moduloSugerido);
      if (this.grupoId === '__auto__') {
        this.grupoId = '__nuevo__';
        this.grupoNuevo = this.nombreFeature.trim() || 'Nuevo módulo';
      }
    }
    const esNuevo = this.grupoId === '__nuevo__';
    const body: any = {
      contenido: this.featurePreview,
      nombre: this.editandoArchivo || this.nombreArchivo(),
      titulo: this.nombreFeature,
      resumen: this.detalle,
      forzarIncompleto: this.forzarIncompleto,
      // Alta nueva: si el preview ya trae Scenario con pasos del mapa, respetarlo
      // (evita que la sanitización vieja recorte el circuito de fallas/cierre).
      respetarGherkin:
        !!this.editandoArchivo ||
        (RX_GHERKIN_SCENARIO.test(this.featurePreview) &&
          RX_GHERKIN_FEATURE.test(this.featurePreview)),
      confirmarSobrescribir: !!this.editandoArchivo
    };
    if (esNuevo) {
      body.grupoNuevo =
        this.grupoNuevo.trim() || this.moduloSugerido.trim() || this.nombreFeature || 'Nuevo módulo';
    } else {
      body.grupoId = this.grupoId;
    }
    try {
      const data = await firstValueFrom(this.api.guardarPruebas(body));
      if (data?.ok === false) {
        if (data.requiereConfirmacion) {
          const ok = await this.askConfirm(
            'Sobrescribir prueba',
            (data.error || 'Ya existe esa prueba.') + '\n\n¿Querés reemplazarla?',
            'Sobrescribir',
            true
          );
          if (!ok) return;
          body.confirmarSobrescribir = true;
          const data2 = await firstValueFrom(this.api.guardarPruebas(body));
          if (data2?.ok === false) {
            this.status.set(data2.error || 'No se pudo guardar.');
            this.statusTipo.set('error');
            return;
          }
          this.status.set(data2.mensaje || 'Prueba guardada.');
          if (data2?.contenido) this.featurePreview = data2.contenido;
          else if (data2?.feature) this.featurePreview = data2.feature;
        } else {
          this.status.set(data.error || 'No se pudo guardar.');
          this.statusTipo.set('error');
          return;
        }
      } else {
        const extra =
          Array.isArray(data?.avisos) && data.avisos.length
            ? ' ' + data.avisos.slice(0, 2).join(' ')
            : data?.stepsAutoCreados
              ? ` Se generaron ${data.stepsAutoCreados} step(s) nuevos.`
              : '';
        this.status.set((data.mensaje || 'Prueba guardada.') + extra);
        if (data?.contenido) this.featurePreview = data.contenido;
        else if (data?.feature) this.featurePreview = data.feature;
      }
      this.statusTipo.set('ok');
      this.editandoArchivo = '';
      if (data?.grupoId) this.grupoId = data.grupoId;
      await this.refreshLista();
    } catch (e: any) {
      this.status.set(e?.error?.error || e?.message || 'Error al guardar');
      this.statusTipo.set('error');
    }
  }

  /**
   * Importa uno o varios Feature (lote de Cursor): crea módulos + casos.
   * Prioriza el Detalle si el usuario pegó Gherkin ahí (es la fuente literal);
   * la vista previa puede tener una versión regenerada por Analizar que lo pisaría.
   */
  async importarLote(): Promise<void> {
    const detalleEsGherkin = RX_GHERKIN_FEATURE.test(this.detalle);
    const contenido = (detalleEsGherkin ? this.detalle.trim() : this.featurePreview.trim() || this.detalle.trim()).trim();
    if (!contenido) {
      this.status.set('Pegá el Gherkin en Detalle o en la vista previa.');
      this.statusTipo.set('warn');
      return;
    }
    if (!RX_GHERKIN_FEATURE.test(contenido) && !/#\s*M[oó]dulo\s*:/i.test(contenido)) {
      this.status.set(
        'El texto no parece Gherkin (falta Feature: o Característica:). Pegá el .feature y volvé a intentar.'
      );
      this.statusTipo.set('warn');
      return;
    }

    const ok = await this.askConfirm(
      'Crear módulo(s) y casos',
      'Se van a crear los Feature del paquete. Cada «# Modulo: …» define o reutiliza un módulo. ¿Continuar?',
      'Crear',
      false
    );
    if (!ok) return;

    try {
      const data = await firstValueFrom(
        this.api.importarLotePruebas({
          contenido,
          forzarIncompleto: this.forzarIncompleto,
          confirmarSobrescribir: this.sobrescribirLote,
          moduloDefault:
            this.grupoId === '__nuevo__'
              ? this.grupoNuevo.trim() || this.moduloSugerido.trim() || undefined
              : this.moduloSugerido.trim() || undefined
        })
      );
      if (data?.ok === false) {
        this.status.set(data.error || 'No se pudo importar el lote.');
        this.statusTipo.set('error');
        return;
      }
      const lista = Array.isArray(data?.creados)
        ? data.creados.map((c: any) => `«${c.titulo || c.nombre}» → ${c.grupoNombre}`).join('; ')
        : '';
      const avisos =
        Array.isArray(data?.avisos) && data.avisos.length ? ' ' + data.avisos.slice(0, 2).join(' ') : '';
      const errs =
        Array.isArray(data?.errores) && data.errores.length
          ? ' Errores: ' + data.errores.slice(0, 3).join(' | ')
          : '';
      this.status.set((data.mensaje || `Se crearon ${data.cantidad || 0} caso(s).`) + (lista ? ' ' + lista : '') + avisos + errs);
      this.statusTipo.set(data?.errores?.length ? 'warn' : 'ok');
      if (!this.featurePreview.trim()) this.featurePreview = contenido;
      await this.refreshLista();
    } catch (e: any) {
      this.status.set(e?.error?.error || e?.message || 'Error al importar lote');
      this.statusTipo.set('error');
    }
  }

  limpiar(): void {
    this.jiraUrl = '';
    this.texto = '';
    this.nombreFeature = '';
    this.detalle = '';
    this.featurePreview = '';
    this.editandoArchivo = '';
    this.grupoNuevo = '';
    this.grupoId = '__auto__';
    this.moduloSugerido = '';
    this.gherkinOrigenArchivo = '';
    this.limpiarDocs();
    this.status.set('');
  }

  toggleGrupo(id: string): void {
    this.abiertos.update((a) => ({ ...a, [id]: !a[id] }));
  }

  toggleRunnerIa(ev: Event): void {
    ev.preventDefault();
    this.runnerIaOpen.update((v) => !v);
  }

  async cargarRunnerIa(): Promise<void> {
    try {
      const d = await firstValueFrom(this.api.getRunnerIaProyecto());
      this.proyectos = d?.proyectos || [];
      this.proyectoActivoId = d?.proyectoActivoId || d?.proyectoActivo?.id || '';
      this.proyectoNombre = d?.proyectoActivo?.nombre || '';
      this.iaAlcanceTexto = d?.iaAlcanceTexto || '';
      this.checklist = d?.checklistDemo || [];
    } catch {
      /* ignore */
    }
    try {
      const m = await firstValueFrom(this.api.getPruebasMapa());
      if (m?.ok) {
        this.mapaIaHint.set(
          `Mapa: ${m.modulos || 0} módulos · ${m.selectores || 0} selectores · ${m.steps || 0} steps · ${m.pistasUi || 0} UI · ${m.consultasDb || 0} consultas DB`
        );
      }
    } catch {
      this.mapaIaHint.set('Mapa aún no generado — tocá «Reentrenar mapa (UI + COBIS/SQL SOT)».');
    }
  }

  async reentrenarMapaIa(): Promise<void> {
    if (this.riaGuardando()) return;
    this.riaGuardando.set(true);
    this.riaStatus.set('Reentrenando mapa (UI + COBIS/SQL SOT)…');
    this.riaStatusTipo.set('info');
    try {
      const r = await firstValueFrom(this.api.postPruebasMapaReentrenar());
      if (r?.ok === false) {
        this.riaStatus.set(r.error || 'No se pudo reentrenar.');
        this.riaStatusTipo.set('error');
        return;
      }
      this.riaStatus.set(r.mensaje || 'Mapa reentrenado.');
      this.riaStatusTipo.set('ok');
      const info = r.info || {};
      this.mapaIaHint.set(
        `Mapa: ${info.modulos || 0} módulos · ${info.selectores || 0} selectores · ${info.steps || 0} steps · ${info.pistasUi || 0} UI · ${info.consultasDb || 0} consultas DB`
      );
    } catch (e: any) {
      this.riaStatus.set(e?.error?.error || e?.message || 'Error al reentrenar mapa');
      this.riaStatusTipo.set('error');
    } finally {
      this.riaGuardando.set(false);
    }
  }

  onProyectoChange(): void {
    const p = this.proyectos.find((x) => x.id === this.proyectoActivoId);
    if (p?.nombre) this.proyectoNombre = p.nombre;
    if (this.proyectoActivoId) this.workspace.elegirProyecto(this.proyectoActivoId);
    void this.guardarRunnerIa();
  }

  abrirFormNuevoProyecto(): void {
    this.proyectoFormModo.set('nuevo');
    this.proyectoFormId = '';
    this.proyectoFormNombre = '';
    this.proyectoFormDescripcion = '';
    this.proyectoFormIaNombre = 'RunnerIA';
    this.runnerIaOpen.set(true);
  }

  abrirFormEditarProyecto(): void {
    const p = this.proyectos.find((x) => x.id === this.proyectoActivoId);
    if (!p) {
      this.toast.warn('Seleccioná un proyecto para modificar.');
      return;
    }
    this.proyectoFormModo.set('editar');
    this.proyectoFormId = p.id;
    this.proyectoFormNombre = p.nombre || '';
    this.proyectoFormDescripcion = p.descripcion || '';
    this.proyectoFormIaNombre = (p as { iaNombre?: string }).iaNombre || 'RunnerIA';
    this.runnerIaOpen.set(true);
  }

  cancelarFormProyecto(): void {
    this.proyectoFormModo.set(null);
  }

  async guardarFormProyecto(): Promise<void> {
    const nombre = this.proyectoFormNombre.trim();
    if (!nombre) {
      this.toast.warn('El nombre del proyecto es obligatorio.');
      return;
    }
    if (this.riaGuardando()) return;
    this.riaGuardando.set(true);
    try {
      const modo = this.proyectoFormModo();
      let r;
      if (modo === 'nuevo') {
        r = await firstValueFrom(
          this.api.postRunnerIaProyecto({
            agregar: {
              id: this.proyectoFormId.trim() || undefined,
              nombre,
              descripcion: this.proyectoFormDescripcion.trim(),
              iaNombre: this.proyectoFormIaNombre.trim() || 'RunnerIA'
            }
          })
        );
      } else if (modo === 'editar') {
        r = await firstValueFrom(
          this.api.postRunnerIaProyecto({
            modificar: {
              id: this.proyectoFormId,
              nombre,
              descripcion: this.proyectoFormDescripcion.trim(),
              iaNombre: this.proyectoFormIaNombre.trim() || 'RunnerIA'
            }
          })
        );
      } else {
        return;
      }
      if (r?.ok === false) {
        this.toast.error(r.error || 'No se pudo guardar el proyecto.');
        return;
      }
      this.proyectoFormModo.set(null);
      await this.cargarRunnerIa();
      if (r?.proyectoActivoId) {
        this.proyectoActivoId = r.proyectoActivoId;
        this.workspace.elegirProyecto(r.proyectoActivoId);
      }
      this.toast.ok(modo === 'nuevo' ? 'Proyecto creado.' : 'Proyecto actualizado.');
    } catch (e: any) {
      this.toast.error(e?.error?.error || 'Error al guardar el proyecto.');
    } finally {
      this.riaGuardando.set(false);
    }
  }

  pedirEliminarProyecto(): void {
    if (!this.proyectoActivoId) {
      this.toast.warn('Seleccioná un proyecto.');
      return;
    }
    if (this.proyectos.length <= 1) {
      this.toast.warn('No se puede eliminar el único proyecto.');
      return;
    }
    this.eliminarProyectoDialog.set(true);
  }

  async confirmarEliminarProyecto(ok: boolean): Promise<void> {
    this.eliminarProyectoDialog.set(false);
    if (!ok || !this.proyectoActivoId) return;
    if (this.riaGuardando()) return;
    this.riaGuardando.set(true);
    try {
      const r = await firstValueFrom(this.api.postRunnerIaProyecto({ eliminar: this.proyectoActivoId }));
      if (r?.ok === false) {
        this.toast.error(r.error || 'No se pudo eliminar.');
        return;
      }
      await this.cargarRunnerIa();
      this.toast.ok('Proyecto eliminado.');
    } catch (e: any) {
      this.toast.error(e?.error?.error || 'Error al eliminar.');
    } finally {
      this.riaGuardando.set(false);
    }
  }

  async guardarRunnerIa(): Promise<void> {
    if (this.riaGuardando()) return;
    this.riaGuardando.set(true);
    this.riaStatus.set('');
    try {
      const r = await firstValueFrom(
        this.api.postRunnerIaProyecto({
          proyectoActivoId: this.proyectoActivoId
        })
      );
      if (r?.ok === false) {
        this.riaStatus.set(r.error || 'No se pudo guardar.');
        this.riaStatusTipo.set('error');
        return;
      }
      this.riaStatus.set(r?.mensaje || 'Proyecto aplicado.');
      this.riaStatusTipo.set('ok');
      await this.cargarRunnerIa();
    } catch (e: any) {
      this.riaStatus.set(e?.error?.error || 'Error al guardar proyecto.');
      this.riaStatusTipo.set('error');
    } finally {
      this.riaGuardando.set(false);
    }
  }

  isOpen(id: string): boolean {
    return !!this.abiertos()[id];
  }

  agregarPrueba(m: ModuloListaItem): void {
    this.menuModuloId.set(null);
    this.prepararFormularioNuevaPrueba(m.id);
    this.tab.set('crear');
    this.abiertos.update((a) => ({ ...a, [m.id]: true }));
    const msg = `Vas a agregar una prueba al módulo «${m.nombre}». Completá el detalle, analizá y guardá.`;
    this.status.set(msg);
    this.statusTipo.set('info');
    this.toast.info(msg);
    this.scrollAlFormularioGenerar();
  }

  abrirRename(m: ModuloListaItem, ev: Event): void {
    ev.preventDefault();
    ev.stopPropagation();
    this.renameId.set(m.id);
    this.renameValue = m.nombre;
    this.abiertos.update((a) => ({ ...a, [m.id]: true }));
  }

  cancelRename(ev?: Event): void {
    ev?.preventDefault();
    ev?.stopPropagation();
    this.renameId.set(null);
    this.renameValue = '';
  }

  async confirmRename(m: ModuloListaItem, ev: Event): Promise<void> {
    ev.preventDefault();
    ev.stopPropagation();
    const r = await this.catalog.renombrarModulo(m.id, this.renameValue);
    if (!r.ok) {
      this.status.set(r.error || 'No se pudo renombrar.');
      this.statusTipo.set('error');
      return;
    }
    this.renameId.set(null);
    this.status.set('Módulo renombrado.');
    this.statusTipo.set('ok');
    await this.refreshLista();
  }

  async eliminarModulo(m: ModuloListaItem, ev: Event): Promise<void> {
    ev.preventDefault();
    ev.stopPropagation();
    const ok = await this.pruebasAdmin.eliminarModulo(
      m,
      (title, body, label, danger) => this.askConfirm(title, body, label, danger),
      (msg, tipo) => {
        this.status.set(msg);
        this.statusTipo.set(tipo);
      }
    );
    if (ok) await this.refreshLista();
  }

  async editarPrueba(p: {
    id: string;
    generada: boolean;
    pruebaArchivo?: string;
    tag?: string;
    titulo: string;
  }): Promise<void> {
    if (p.generada && p.pruebaArchivo) {
      try {
        const data = await firstValueFrom(this.api.getPruebasBorrador(p.pruebaArchivo));
        if (data?.ok === false) {
          this.status.set(data.error || 'No se pudo abrir.');
          this.statusTipo.set('error');
          return;
        }
        this.featurePreview = data.contenido || '';
        this.nombreFeature = (data.titulo || p.titulo || '').replace(/^Prueba\s*[—\-–:]?\s*/i, '');
        this.detalle = data.descripcion || data.corto || '';
        this.editandoArchivo = data.nombre || p.pruebaArchivo;
        if (data.grupoId) this.grupoId = data.grupoId;
        this.status.set('Escenario cargado. Modificá y guardá, o eliminá.');
        this.statusTipo.set('ok');
        this.scrollAlFormularioGenerar();
      } catch (e: any) {
        this.status.set(e?.message || 'Error al abrir');
        this.statusTipo.set('error');
      }
      return;
    }
    // Suite: cargar Gherkin por tag (vista/edición local; guardar suite no está en API Angular completa)
    const tag = String(p.tag || '')
      .split(/\s+/)[0]
      .replace(/^@/, '');
    if (!tag) {
      this.status.set('Sin tag para cargar el feature de suite.');
      this.statusTipo.set('warn');
      return;
    }
    try {
      const data = await firstValueFrom(this.api.getSuiteFeatureByTag(tag));
      if (data?.ok === false) {
        this.status.set(data.error || 'No se pudo cargar el feature.');
        this.statusTipo.set('error');
        return;
      }
      this.featurePreview = data.contenido || '';
      this.nombreFeature = p.titulo;
      this.editandoArchivo = '';
      this.status.set(
        'Feature de suite cargado (solo lectura en Generar). Para quitarlo del Runner usá Eliminar.'
      );
      this.statusTipo.set('info');
      this.scrollAlFormularioGenerar();
    } catch (e: any) {
      this.status.set(e?.message || 'Error al cargar suite');
      this.statusTipo.set('error');
    }
  }

  async eliminarPrueba(p: {
    id: string;
    generada: boolean;
    pruebaArchivo?: string;
    titulo: string;
  }): Promise<void> {
    const ok = await this.pruebasAdmin.eliminarPrueba(
      p,
      (title, body, label, danger) => this.askConfirm(title, body, label, danger),
      (msg, tipo) => {
        this.status.set(msg);
        this.statusTipo.set(tipo);
      }
    );
    if (ok) await this.refreshLista();
  }

  askConfirm(
    title: string,
    body: string,
    confirmLabel = 'Confirmar',
    danger = false
  ): Promise<boolean> {
    this.dialogTitle.set(title);
    this.dialogBody.set(body);
    this.dialogConfirmLabel.set(confirmLabel);
    this.dialogDanger.set(danger);
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

  async toggleGherkin(p: {
    id: string;
    generada: boolean;
    pruebaArchivo?: string;
    tag?: string;
  }): Promise<void> {
    const cur = this.gherkinOpen();
    if (cur[p.id] !== undefined) {
      const next = { ...cur };
      delete next[p.id];
      this.gherkinOpen.set(next);
      return;
    }
    this.gherkinOpen.update((m) => ({ ...m, [p.id]: 'Cargando…' }));
    try {
      if (p.generada && p.pruebaArchivo) {
        const data = await firstValueFrom(this.api.getPruebasBorrador(p.pruebaArchivo));
        this.gherkinOpen.update((m) => ({
          ...m,
          [p.id]: data.contenido || data.error || '(sin contenido)'
        }));
      } else {
        const tag = String(p.tag || '')
          .split(/\s+/)[0]
          .replace(/^@/, '');
        const data = await firstValueFrom(this.api.getSuiteFeatureByTag(tag));
        this.gherkinOpen.update((m) => ({
          ...m,
          [p.id]: data.contenido || data.error || '(sin contenido)'
        }));
      }
    } catch (e: any) {
      this.gherkinOpen.update((m) => ({ ...m, [p.id]: e?.message || 'Error' }));
    }
  }

  async deshacer(): Promise<void> {
    const ok = await this.pruebasAdmin.deshacer((msg, tipo) => {
      this.status.set(msg);
      this.statusTipo.set(tipo);
    });
    if (ok) await this.refreshLista();
  }

  ejecutarRegresion(): void {
    void this.router.navigate(['/runner'], { queryParams: QUERY_REGRESION_COMPLETA });
  }
}
