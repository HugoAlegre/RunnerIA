import { Component, HostListener, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { ToastService } from '../../core/services/toast.service';
import {
  CatalogService,
  ModuloListaItem
} from '../../core/services/catalog.service';
import { PruebasAdminService } from '../../core/services/pruebas-admin.service';
import { ConfigCliente, UsuarioSot } from '../../core/models';
import { ConfirmDialogComponent } from '../../shared/confirm-dialog/confirm-dialog.component';
import { PasswordFieldComponent } from '../../shared/password-field/password-field.component';
import { PageTocComponent, PageTocItem } from '../../shared/page-toc/page-toc.component';
import { ModulosListaComponent } from '../generar/panels/modulos-lista.component';
import {
  WorkspaceProfileMode,
  WorkspaceProfileService
} from '../../core/services/workspace-profile.service';

const SUCURSALES_SOT = [
  { codigo: '30', nombre: '25 de Mayo' },
  { codigo: '79', nombre: 'Centro Comercial El Punto' },
  { codigo: '113', nombre: 'Cafayate' },
  { codigo: '115', nombre: 'Cachi' },
  { codigo: '120', nombre: 'Batalla de Salta' },
  { codigo: '121', nombre: 'Cerrillos' },
  { codigo: '128', nombre: 'Aguaray' },
  { codigo: '131', nombre: 'San Martin' },
  { codigo: '133', nombre: 'Shopping' },
  { codigo: '153', nombre: 'Apolinario Saravia' },
  { codigo: '196', nombre: 'Alvarado' },
  { codigo: '301', nombre: 'Cordoba' },
  { codigo: '324', nombre: 'Alem' },
  { codigo: '351', nombre: 'Villa Allende' },
  { codigo: '481', nombre: 'Banfield' },
  { codigo: '501', nombre: 'Flores Norte' },
  { codigo: '540', nombre: 'Casa Central' },
  { codigo: '544', nombre: 'Villa Regina' },
  { codigo: '558', nombre: 'Cipoletti' },
  { codigo: '682', nombre: 'Adrogue' },
  { codigo: '702', nombre: 'Acebal' },
  { codigo: '811', nombre: 'Casa Matriz' },
  { codigo: '872', nombre: 'Arroyito Cordoba' }
];

@Component({
  selector: 'app-config-page',
  standalone: true,
  imports: [CommonModule, FormsModule, ConfirmDialogComponent, PasswordFieldComponent, PageTocComponent, ModulosListaComponent, RouterLink],
  templateUrl: './config-page.component.html',
  styleUrl: './config-page.component.scss'
})
export class ConfigPageComponent implements OnInit {
  private api = inject(ApiService);
  private toast = inject(ToastService);
  private router = inject(Router);
  private pruebasAdmin = inject(PruebasAdminService);
  readonly workspace = inject(WorkspaceProfileService);
  readonly catalog = inject(CatalogService);

  readonly tocItems = computed<PageTocItem[]>(() => {
    const base: PageTocItem[] = [
      { id: 'cfg-perfil', label: 'Perfil del proyecto' },
      { id: 'cfg-modulos', label: 'Módulos y casos' },
      { id: 'cfg-ambiente', label: 'Ambiente / URL' },
      { id: 'cfg-acceso', label: 'Acceso app' },
      { id: 'cfg-sql', label: 'SQL' },
      { id: 'cfg-cobis', label: this.workspace.esSotAccusys() ? 'COBIS / host' : 'Host BD' },
      { id: 'cfg-jira', label: 'Jira' },
      { id: 'cfg-xray', label: 'Xray' },
      { id: 'cfg-llm', label: 'LLM' }
    ];
    if (this.workspace.esSotAccusys()) {
      base.splice(3, 0, { id: 'cfg-usuarios', label: 'Usuarios' });
      base.splice(5, 0, { id: 'cfg-sc416', label: 'SC-416 cajas' });
      base.splice(6, 0, { id: 'cfg-retiro', label: 'Retiro efectivo' });
      base.push({ id: 'cfg-repos', label: 'Repos' });
    }
    return base;
  });

  status = signal('');
  statusTipo = signal('info');
  sqlStatus = signal('');
  sqlStatusTipo = signal('info');
  cobisStatus = signal('');
  cobisStatusTipo = signal('info');
  jiraStatus = signal('');
  jiraStatusTipo = signal('info');
  probandoSql = signal(false);
  probandoCobis = signal(false);
  probandoJira = signal(false);

  repoExpandido = signal<Record<number, boolean>>({});

  dialogOpen = signal(false);
  dialogOk = signal(true);
  dialogTitle = signal('');
  dialogBody = signal('');

  readonly sucursales = SUCURSALES_SOT;

  ambiente: 'Dev' | 'QA' | 'Custom' = 'Dev';
  urlInicio = '';
  authOpenIdUrl = '';
  permitirAuthQa = false;
  ajustarSqlAmbiente = false;
  sucursal = '';
  codigoSucursal = '';
  usuario = '';
  passwordLogin = '';
  tienePasswordLogin = false;

  usuariosSot: UsuarioSot[] = [{ rol: 'SOT Supervisor', usuario: '', password: '', tienePassword: false }];

  sqlSotServidor = '';
  sqlSotPuerto = '1433';
  sqlSotUsuario = '';
  sqlSotBaseDatos = 'UW_CASHIER';
  passwordSqlSot = '';
  tienePasswordSqlSot = false;

  cobisServidor = '';
  cobisHost = '';
  puertoSybase = '7410';
  cobisUsuario = '';
  cobisBaseDatos = 'cobis';
  passwordCobis = '';
  tienePasswordCobis = false;

  /** Xray (Cloud/Server) — credenciales solo en sessionStorage. */
  xrayBaseUrl = '';
  xrayClientId = '';
  xrayClientSecret = '';
  rememberXray = false;

  jiraEmail = '';
  jiraToken = '';
  jiraUrl = '';
  rememberJira = true;
  usarLlm = false;
  llmKey = '';
  llmModel = 'gpt-4o-mini';
  llmBase = '';
  rememberLlm = false;

  /** SC-416 — correlativos usados en Cierre de Sucursal */
  cierreForzadoNotaCompensada = '4161';
  cierreForzadoNotaNormal = '4162';
  cierreForzadoNotaMiniBoveda = '';

  /** Retiro de efectivo — COBIS + importe aleatorio (appsettings) */
  cobisConsultaMaxCuentas = '50';
  cobisConsultaModoRotacion: 'Aleatoria' | 'RoundRobin' | 'Primera' = 'Aleatoria';
  cobisConsultaRotacionPorCliente = 'true';
  cobisConsultaUnaCuentaPorCliente = 'true';
  cobisConsultaSaldoMinimoPesos = '1000';
  cobisConsultaSaldoMinimoExtranjera = '100';
  cobisConsultaSaldoMinimoCategorizada = '100';
  cobisConsultaTitularidadDefault: 'Individual' | 'Conjunta' | 'Indistinta' | 'Categorizada' = 'Individual';
  retiroImporteMinimo = '100';
  retiroImporteMaximoPractico = '500';
  retiroMargenSaldoResiduo = '1';
  retiroPorcentajeMinimo = '0.05';
  retiroPorcentajeMaximo = '0.40';

  permitirGitPull = true;
  ramaObjetivo = 'develop';
  tfsUsuario = '';
  tfsPat = '';
  tienePatTfs = false;
  syncingCatalogo = signal(false);
  catalogoResumen = signal('');
  catalogoGenerado = signal('');
  confirmEliminarOpen = signal(false);
  confirmEliminarIndex = signal(-1);
  confirmEliminarNombre = signal('');
  modulos = signal<ModuloListaItem[]>([]);
  modulosAbiertos = signal<Record<string, boolean>>({});
  menuModuloId = signal<string | null>(null);
  renameId = signal<string | null>(null);
  renameValue = '';
  gherkinOpen = signal<Record<string, string>>({});
  puedeDeshacerModulos = signal(false);
  deshacerHintModulos = signal('');
  confirmPruebaOpen = signal(false);
  confirmPruebaTitle = signal('');
  confirmPruebaBody = signal('');
  confirmPruebaLabel = signal('Confirmar');
  confirmPruebaDanger = signal(false);
  private confirmPruebaResolver: ((ok: boolean) => void) | null = null;
  catalogoRepos: Array<{
    id: string;
    nombre: string;
    rol: string;
    urlRemota?: string;
    rutaEnmascarada?: string;
    usaCache?: boolean;
    estado?: string;
    existe?: boolean;
    rama?: string;
    ramaObjetivo?: string;
    commitCorto?: string;
    atrasadoVsObjetivo?: string;
    okPull?: boolean | null;
    aviso?: string;
    conteos?: { rutas?: number; endpoints?: number; modelos?: number; pantallas?: number };
  }> = [];

  async ngOnInit(): Promise<void> {
    this.cargarLocal();
    await this.recargar();
    await this.cargarCatalogo();
    await this.refreshModulos();
  }

  async recargar(): Promise<void> {
    try {
      const c = await firstValueFrom(this.api.getConfigCliente());
      this.ambiente = (['Dev', 'QA', 'Custom'].includes(String(c.ambiente))
        ? c.ambiente
        : 'Custom') as 'Dev' | 'QA' | 'Custom';
      this.urlInicio = String(c.urlInicio || '');
      this.authOpenIdUrl = String(c.authOpenIdUrl || '');
      this.permitirAuthQa = !!c.permitirAuthQa;
      this.sucursal = String(c.sucursal || '');
      this.codigoSucursal = String(c.codigoSucursal || '');
      this.usuario = String(c.usuario || '');
      this.tienePasswordLogin = !!c.tienePasswordLogin;
      this.passwordLogin = '';

      const lista = c.usuariosSot || [];
      this.usuariosSot = lista.length
        ? lista.map((u) => ({
            rol: u.rol || 'SOT Supervisor',
            usuario: u.usuario || '',
            password: '',
            tienePassword: !!u.tienePassword
          }))
        : [{ rol: 'SOT Supervisor', usuario: '', password: '', tienePassword: false }];

      this.sqlSotServidor = String(c.sqlSotServidor || '');
      this.sqlSotPuerto = String(c.sqlSotPuerto || '1433');
      this.sqlSotUsuario = String(c.sqlSotUsuario || '');
      this.sqlSotBaseDatos = String(c.sqlSotBaseDatos || 'UW_CASHIER');
      this.tienePasswordSqlSot = !!c.tienePasswordSqlSot;
      this.passwordSqlSot = '';

      this.cobisServidor = String(c.cobisServidor || '');
      this.cobisHost = String(c.cobisHost || '');
      this.puertoSybase = String(c.puertoSybase || '7410');
      this.cobisUsuario = String(c.cobisUsuario || '');
      this.cobisBaseDatos = String(c.cobisBaseDatos || 'cobis');
      this.tienePasswordCobis = !!c.tienePasswordCobis;
      this.passwordCobis = '';

      this.cierreForzadoNotaCompensada = String(c.cierreForzadoNotaCompensada || '4161');
      this.cierreForzadoNotaNormal = String(c.cierreForzadoNotaNormal || '4162');
      this.cierreForzadoNotaMiniBoveda = String(c.cierreForzadoNotaMiniBoveda || '');

      this.cobisConsultaMaxCuentas = String(c.cobisConsultaMaxCuentas || '50');
      this.cobisConsultaModoRotacion = this.parseModoRotacion(String(c.cobisConsultaModoRotacion || 'Aleatoria'));
      this.cobisConsultaRotacionPorCliente = String(c.cobisConsultaRotacionPorCliente ?? 'true');
      this.cobisConsultaUnaCuentaPorCliente = String(c.cobisConsultaUnaCuentaPorCliente ?? 'true');
      this.cobisConsultaSaldoMinimoPesos = String(c.cobisConsultaSaldoMinimoPesos || '1000');
      this.cobisConsultaSaldoMinimoExtranjera = String(c.cobisConsultaSaldoMinimoExtranjera || '100');
      this.cobisConsultaSaldoMinimoCategorizada = String(c.cobisConsultaSaldoMinimoCategorizada || '100');
      this.cobisConsultaTitularidadDefault = this.parseTitularidadDefault(
        String(c.cobisConsultaTitularidadDefault || 'Individual')
      );
      this.retiroImporteMinimo = String(c.retiroImporteMinimo || '100');
      this.retiroImporteMaximoPractico = String(c.retiroImporteMaximoPractico || '500');
      this.retiroMargenSaldoResiduo = String(c.retiroMargenSaldoResiduo || '1');
      this.retiroPorcentajeMinimo = String(c.retiroPorcentajeMinimo || '0.05');
      this.retiroPorcentajeMaximo = String(c.retiroPorcentajeMaximo || '0.40');

      this.onAmbienteChange(false);
      this.status.set('Configuración cargada.');
      this.statusTipo.set('info');
    } catch {
      this.status.set('No se pudo cargar config (¿servidor offline?).');
      this.statusTipo.set('warn');
    }
  }

  onAmbienteChange(forzarUrl = true): void {
    // Presets Accusys solo en perfil SOT; en genérico no se autocompletan hosts.
    if (!this.workspace.esSotAccusys()) {
      if (forzarUrl && this.ambiente === 'Custom') {
        /* el operador escribe todo a mano */
      }
      return;
    }
    const preset = this.workspace.presetAmbiente(this.ambiente);
    if (forzarUrl && this.ambiente !== 'Custom' && preset.url) this.urlInicio = preset.url;
    if (this.ambiente === 'QA' && !this.authOpenIdUrl) this.authOpenIdUrl = preset.authOpenIdUrl;
    if (forzarUrl && this.ajustarSqlAmbiente && this.ambiente !== 'Custom' && preset.sqlServidor) {
      this.sqlSotServidor = preset.sqlServidor;
    }
  }

  onSucursalChange(): void {
    const valor = this.sucursal.trim();
    const match = this.sucursales.find((s) => valor === `${s.codigo} - ${s.nombre}`);
    if (match) {
      this.codigoSucursal = match.codigo;
      return;
    }
    const pref = valor.match(/^(\d+)\s*-/);
    if (pref) this.codigoSucursal = pref[1];
  }

  agregarUsuarioSot(): void {
    this.usuariosSot = [
      ...this.usuariosSot,
      { rol: 'SOT Supervisor', usuario: '', password: '', tienePassword: false }
    ];
  }

  quitarUsuarioSot(i: number): void {
    if (this.usuariosSot.length <= 1) return;
    this.usuariosSot = this.usuariosSot.filter((_, idx) => idx !== i);
  }

  abrirAuthQa(): void {
    const fallback = this.workspace.esSotAccusys()
      ? this.workspace.presetAmbiente('QA').authOpenIdUrl
      : '';
    const url = this.authOpenIdUrl || fallback;
    if (!url) {
      this.toast.warn('Completá la URL OpenID en Configuración.');
      return;
    }
    window.open(url, '_blank', 'noopener');
  }

  /** Cambia perfil SOT Accusys ↔ genérico sin borrar casos ni secrets del servidor. */
  aplicarPerfil(mode: WorkspaceProfileMode): void {
    this.workspace.cambiar(mode);
    if (mode === 'generico') {
      this.ambiente = 'Custom';
      this.ajustarSqlAmbiente = false;
      this.toast.ok('Perfil genérico: completá URL, SQL, COBIS/host y Jira/Xray vos mismo.');
    } else {
      if (this.ambiente === 'Custom') this.ambiente = 'Dev';
      this.onAmbienteChange(true);
      if (!this.sqlSotBaseDatos) this.sqlSotBaseDatos = 'UW_CASHIER';
      if (!this.puertoSybase) this.puertoSybase = '7410';
      if (!this.cobisBaseDatos) this.cobisBaseDatos = 'cobis';
      this.toast.ok('Perfil SOT Accusys: presets DEV/QA disponibles.');
    }
  }

  vaciarCamposInstancia(): void {
    this.ambiente = 'Custom';
    this.urlInicio = '';
    this.authOpenIdUrl = '';
    this.permitirAuthQa = false;
    this.sqlSotServidor = '';
    this.sqlSotUsuario = '';
    this.sqlSotBaseDatos = '';
    this.cobisServidor = '';
    this.cobisHost = '';
    this.cobisUsuario = '';
    this.cobisBaseDatos = '';
    this.puertoSybase = '';
    this.toast.info('Campos de instancia vaciados. Guardá cuando completes los nuevos.');
  }

  private cargarLocal(): void {
    try {
      // Migración: no dejar tokens/API keys en localStorage (sobreviven al cierre).
      this.purgarSecretosLocalStorage();

      const j = JSON.parse(
        sessionStorage.getItem('sot-runner-pruebas-jira') ||
          localStorage.getItem('sot-runner-pruebas-jira-meta') ||
          'null'
      );
      if (j) {
        this.jiraEmail = j.email || '';
        this.jiraToken = j.token || '';
        this.jiraUrl = (j.url || '').trim();
        this.rememberJira = !!sessionStorage.getItem('sot-runner-pruebas-jira') || !!j.email;
      }
      if (!this.jiraUrl) {
        this.jiraUrl = (localStorage.getItem('sot-runner-pruebas-jira-url') || '').trim();
      }
      const l = JSON.parse(
        sessionStorage.getItem('sot-runner-pruebas-llm') ||
          localStorage.getItem('sot-runner-pruebas-llm-meta') ||
          'null'
      );
      if (l) {
        this.usarLlm = !!l.enabled;
        this.llmKey = l.apiKey || '';
        this.llmModel = l.model || 'gpt-4o-mini';
        this.llmBase = l.baseUrl || '';
        this.rememberLlm = !!sessionStorage.getItem('sot-runner-pruebas-llm') || !!l.enabled;
      }
      const x = JSON.parse(
        sessionStorage.getItem('sot-runner-pruebas-xray') ||
          localStorage.getItem('sot-runner-pruebas-xray-meta') ||
          'null'
      );
      if (x) {
        this.xrayBaseUrl = x.baseUrl || '';
        this.xrayClientId = x.clientId || '';
        this.xrayClientSecret = x.clientSecret || '';
        this.rememberXray = !!sessionStorage.getItem('sot-runner-pruebas-xray') || !!x.baseUrl;
      }
    } catch {
      /* ignore */
    }
  }

  /** Elimina tokens/keys legacy de localStorage; deja solo metadatos no sensibles. */
  private purgarSecretosLocalStorage(): void {
    try {
      const jRaw = localStorage.getItem('sot-runner-pruebas-jira');
      if (jRaw) {
        const j = JSON.parse(jRaw);
        localStorage.setItem(
          'sot-runner-pruebas-jira-meta',
          JSON.stringify({ email: j?.email || '', url: (j?.url || '').trim() })
        );
        localStorage.removeItem('sot-runner-pruebas-jira');
        if (j?.token) {
          sessionStorage.setItem(
            'sot-runner-pruebas-jira',
            JSON.stringify({ email: j.email || '', token: j.token, url: (j.url || '').trim() })
          );
        }
      }
      const lRaw = localStorage.getItem('sot-runner-pruebas-llm');
      if (lRaw) {
        const l = JSON.parse(lRaw);
        localStorage.setItem(
          'sot-runner-pruebas-llm-meta',
          JSON.stringify({
            enabled: !!l?.enabled,
            model: l?.model || 'gpt-4o-mini',
            baseUrl: l?.baseUrl || ''
          })
        );
        localStorage.removeItem('sot-runner-pruebas-llm');
        if (l?.apiKey) {
          sessionStorage.setItem(
            'sot-runner-pruebas-llm',
            JSON.stringify({
              enabled: !!l.enabled,
              apiKey: l.apiKey,
              model: l.model || 'gpt-4o-mini',
              baseUrl: l.baseUrl || ''
            })
          );
        }
      }
    } catch {
      /* ignore */
    }
  }

  private guardarLocal(): void {
    try {
      localStorage.removeItem('sot-runner-pruebas-jira');
      localStorage.removeItem('sot-runner-pruebas-llm');
      if (this.rememberJira) {
        // Metadatos en localStorage; token solo en sessionStorage (se borra al cerrar el navegador).
        localStorage.setItem(
          'sot-runner-pruebas-jira-meta',
          JSON.stringify({ email: this.jiraEmail, url: this.jiraUrl.trim() })
        );
        sessionStorage.setItem(
          'sot-runner-pruebas-jira',
          JSON.stringify({
            email: this.jiraEmail,
            token: this.jiraToken,
            url: this.jiraUrl.trim()
          })
        );
      } else {
        localStorage.removeItem('sot-runner-pruebas-jira-meta');
        sessionStorage.removeItem('sot-runner-pruebas-jira');
      }
      this.persistirJiraUrl();
      if (this.rememberLlm) {
        localStorage.setItem(
          'sot-runner-pruebas-llm-meta',
          JSON.stringify({
            enabled: this.usarLlm,
            model: this.llmModel,
            baseUrl: this.llmBase
          })
        );
        sessionStorage.setItem(
          'sot-runner-pruebas-llm',
          JSON.stringify({
            enabled: this.usarLlm,
            apiKey: this.llmKey,
            model: this.llmModel,
            baseUrl: this.llmBase
          })
        );
      } else {
        localStorage.removeItem('sot-runner-pruebas-llm-meta');
        sessionStorage.removeItem('sot-runner-pruebas-llm');
      }
      if (this.rememberXray) {
        localStorage.setItem(
          'sot-runner-pruebas-xray-meta',
          JSON.stringify({
            baseUrl: this.xrayBaseUrl.trim(),
            clientId: this.xrayClientId.trim()
          })
        );
        sessionStorage.setItem(
          'sot-runner-pruebas-xray',
          JSON.stringify({
            baseUrl: this.xrayBaseUrl.trim(),
            clientId: this.xrayClientId.trim(),
            clientSecret: this.xrayClientSecret
          })
        );
      } else {
        localStorage.removeItem('sot-runner-pruebas-xray-meta');
        sessionStorage.removeItem('sot-runner-pruebas-xray');
      }
    } catch {
      /* ignore */
    }
  }

  onJiraUrlChange(): void {
    this.persistirJiraUrl();
    if (!this.rememberJira) return;
    try {
      localStorage.setItem(
        'sot-runner-pruebas-jira-meta',
        JSON.stringify({ email: this.jiraEmail, url: this.jiraUrl.trim() })
      );
      sessionStorage.setItem(
        'sot-runner-pruebas-jira',
        JSON.stringify({
          email: this.jiraEmail,
          token: this.jiraToken,
          url: this.jiraUrl.trim()
        })
      );
    } catch {
      /* ignore */
    }
  }

  private persistirJiraUrl(): void {
    try {
      const u = this.jiraUrl.trim();
      if (u) localStorage.setItem('sot-runner-pruebas-jira-url', u);
      else localStorage.removeItem('sot-runner-pruebas-jira-url');
    } catch {
      /* ignore */
    }
  }

  private bodyCliente(): ConfigCliente {
    return {
      ambiente: this.ambiente,
      urlInicio: this.urlInicio,
      authOpenIdUrl: this.authOpenIdUrl,
      permitirAuthQa: this.permitirAuthQa,
      sucursal: this.sucursal,
      codigoSucursal: this.codigoSucursal,
      usuario: this.usuario,
      passwordLogin: this.passwordLogin || undefined,
      usuariosSot: this.usuariosSot
        .filter((u) => (u.usuario || '').trim())
        .map((u) => ({
          rol: (u.rol || 'SOT Supervisor').trim(),
          usuario: (u.usuario || '').trim(),
          password: u.password || undefined
        })),
      sqlSotServidor: this.sqlSotServidor,
      sqlSotPuerto: this.sqlSotPuerto,
      sqlSotUsuario: this.sqlSotUsuario,
      sqlSotBaseDatos: this.sqlSotBaseDatos,
      passwordSqlSot: this.passwordSqlSot || undefined,
      cobisServidor: this.cobisServidor,
      cobisHost: this.cobisHost,
      puertoSybase: this.puertoSybase,
      cobisUsuario: this.cobisUsuario,
      cobisBaseDatos: this.cobisBaseDatos,
      passwordCobis: this.passwordCobis || undefined,
      cierreForzadoNotaCompensada: this.cierreForzadoNotaCompensada,
      cierreForzadoNotaNormal: this.cierreForzadoNotaNormal,
      cierreForzadoNotaMiniBoveda: this.cierreForzadoNotaMiniBoveda,
      cobisConsultaMaxCuentas: this.cobisConsultaMaxCuentas,
      cobisConsultaModoRotacion: this.cobisConsultaModoRotacion,
      cobisConsultaRotacionPorCliente: this.cobisConsultaRotacionPorCliente,
      cobisConsultaUnaCuentaPorCliente: this.cobisConsultaUnaCuentaPorCliente,
      cobisConsultaSaldoMinimoPesos: this.cobisConsultaSaldoMinimoPesos,
      cobisConsultaSaldoMinimoExtranjera: this.cobisConsultaSaldoMinimoExtranjera,
      cobisConsultaSaldoMinimoCategorizada: this.cobisConsultaSaldoMinimoCategorizada,
      cobisConsultaTitularidadDefault: this.cobisConsultaTitularidadDefault,
      retiroImporteMinimo: this.retiroImporteMinimo,
      retiroImporteMaximoPractico: this.retiroImporteMaximoPractico,
      retiroMargenSaldoResiduo: this.retiroMargenSaldoResiduo,
      retiroPorcentajeMinimo: this.retiroPorcentajeMinimo,
      retiroPorcentajeMaximo: this.retiroPorcentajeMaximo
    };
  }

  private parseModoRotacion(valor: string): 'Aleatoria' | 'RoundRobin' | 'Primera' {
    const v = valor.trim();
    if (v === 'RoundRobin' || v === 'Primera') return v;
    return 'Aleatoria';
  }

  private parseTitularidadDefault(
    valor: string
  ): 'Individual' | 'Conjunta' | 'Indistinta' | 'Categorizada' {
    const v = valor.trim();
    if (v === 'Conjunta' || v === 'Indistinta' || v === 'Categorizada') return v;
    return 'Individual';
  }

  async guardar(): Promise<void> {
    if (this.ambiente === 'QA' && this.workspace.esSotAccusys()) {
      if (!this.authOpenIdUrl.trim()) {
        this.status.set('En QA Accusys hace falta la URL Auth OpenID.');
        this.statusTipo.set('warn');
        this.toast.warn('Falta URL Auth OpenID');
        return;
      }
      if (!this.permitirAuthQa) {
        this.status.set('Marcá el permiso Auth QA para guardar.');
        this.statusTipo.set('warn');
        this.toast.warn('Marcá permiso Auth QA');
        return;
      }
    }
    this.guardarLocal();
    try {
      const r = await firstValueFrom(this.api.postConfigCliente(this.bodyCliente()));
      if (r?.ok === false) {
        this.status.set(r.error || 'No se pudo guardar.');
        this.statusTipo.set('error');
        this.toast.error(r.error || 'No se pudo guardar');
        return;
      }
      this.status.set(r?.mensaje || 'Configuración guardada.');
      this.statusTipo.set('ok');
      this.toast.ok('Configuración guardada');
      await this.recargar();
    } catch (e: any) {
      this.status.set(e?.error?.error || e?.message || 'Error de red');
      this.statusTipo.set('error');
      this.toast.error(e?.error?.error || e?.message || 'Error de red');
    }
  }

  async probarJira(): Promise<void> {
    if (this.probandoJira()) return;
    const email = this.jiraEmail.trim();
    const token = this.jiraToken.trim();
    if (!email || !token) {
      const err =
        'Completá email y API token de Jira. Creá el token en id.atlassian.com → API tokens.';
      this.setStatus(err, 'warn');
      this.jiraStatus.set(err);
      this.jiraStatusTipo.set('warn');
      this.abrirDialogo(false, 'Faltan credenciales Jira', err);
      return;
    }

    let jiraUrl = this.jiraUrl.trim();
    if (!jiraUrl) {
      try {
        jiraUrl = (localStorage.getItem('sot-runner-pruebas-jira-url') || '').trim();
      } catch {
        jiraUrl = '';
      }
    }
    if (!jiraUrl) {
      const prompted =
        window.prompt(
          'Para sincronizar/probar, pegá un link de ticket Jira (ej. https://dominio.atlassian.net/browse/SC-161):',
          ''
        ) || '';
      jiraUrl = prompted.trim();
      if (jiraUrl) {
        this.jiraUrl = jiraUrl;
        this.onJiraUrlChange();
      }
    }
    if (!jiraUrl) {
      const err = 'Hace falta un link de ticket para sincronizar la conexión con Jira.';
      this.setStatus(err, 'warn');
      this.jiraStatus.set(err);
      this.jiraStatusTipo.set('warn');
      return;
    }

    this.guardarLocal();
    this.probandoJira.set(true);
    this.setStatus('Sincronizando / probando conexión con Jira…', 'info');
    this.jiraStatus.set('Sincronizando…');
    this.jiraStatusTipo.set('info');
    try {
      const r = await firstValueFrom(
        this.api.probarJira({ jiraUrl, jiraEmail: email, jiraToken: token })
      );
      if (r?.ok === false) {
        const err = r.error || 'No se pudo conectar con Jira.';
        this.setStatus(err, 'error');
        this.jiraStatus.set(err);
        this.jiraStatusTipo.set('error');
        this.abrirDialogo(false, 'No se pudo sincronizar con Jira', err);
        return;
      }
      const extra = r.estado ? ` · Estado: ${r.estado}` : '';
      const detalle = (r.mensaje || `OK · ${r.key || ''}`) + extra;
      this.setStatus(detalle, 'ok');
      this.jiraStatus.set(detalle);
      this.jiraStatusTipo.set('ok');
      this.abrirDialogo(true, 'Jira sincronizado', detalle);
    } catch (e: any) {
      const err = this.extraerError(e, 'Error al sincronizar con Jira');
      this.setStatus(err, 'error');
      this.jiraStatus.set(err);
      this.jiraStatusTipo.set('error');
      this.abrirDialogo(false, 'No se pudo sincronizar con Jira', err);
    } finally {
      this.probandoJira.set(false);
    }
  }

  async probarSqlSot(): Promise<void> {
    if (this.probandoSql()) return;
    if (!this.passwordSqlSot.trim() && !this.tienePasswordSqlSot) {
      const err =
        'No hay contraseña SQL SOT guardada. Escribila en el campo y pulsá «Guardar configuración», o completala y probá de nuevo.';
      this.setStatus(err, 'warn');
      this.sqlStatus.set(err);
      this.sqlStatusTipo.set('warn');
      this.abrirDialogo(false, 'Falta contraseña SQL SOT', err);
      return;
    }
    this.probandoSql.set(true);
    this.setStatus('Probando conexión SQL SOT…', 'info');
    this.sqlStatus.set('Probando…');
    this.sqlStatusTipo.set('info');
    try {
      const r = await firstValueFrom(this.api.probarSqlSot(this.bodyCliente()));
      if (r?.ok === false) {
        const err = r.error || 'No se pudo conectar a SQL SOT.';
        this.setStatus(err, 'error');
        this.sqlStatus.set(err);
        this.sqlStatusTipo.set('error');
        this.abrirDialogo(false, 'No se pudo conectar a SQL SOT', err);
        return;
      }
      const detalle = this.armarDetalleConexion('SQL SOT', r);
      this.setStatus(detalle, 'ok');
      this.sqlStatus.set('Conectado correctamente');
      this.sqlStatusTipo.set('ok');
      this.abrirDialogo(true, 'Conectado a SQL SOT', detalle);
    } catch (e: any) {
      const err = this.extraerError(e, 'Error al probar SQL SOT');
      this.setStatus(err, 'error');
      this.sqlStatus.set(err);
      this.sqlStatusTipo.set('error');
      this.abrirDialogo(false, 'No se pudo conectar a SQL SOT', err);
    } finally {
      this.probandoSql.set(false);
    }
  }

  async probarCobis(): Promise<void> {
    if (this.probandoCobis()) return;
    if (!this.passwordCobis.trim() && !this.tienePasswordCobis) {
      const err =
        'No hay contraseña COBIS guardada. Escribila en el campo y pulsá «Guardar configuración», o completala y probá de nuevo.';
      this.setStatus(err, 'warn');
      this.cobisStatus.set(err);
      this.cobisStatusTipo.set('warn');
      this.abrirDialogo(false, 'Falta contraseña COBIS', err);
      return;
    }
    this.probandoCobis.set(true);
    this.setStatus('Probando conexión COBIS/Sybase…', 'info');
    this.cobisStatus.set('Probando…');
    this.cobisStatusTipo.set('info');
    try {
      const r = await firstValueFrom(this.api.probarCobis(this.bodyCliente()));
      if (r?.ok === false) {
        const err = r.error || 'No se pudo conectar a COBIS.';
        this.setStatus(err, 'error');
        this.cobisStatus.set(err);
        this.cobisStatusTipo.set('error');
        this.abrirDialogo(false, 'No se pudo conectar a COBIS', err);
        return;
      }
      const detalle = this.armarDetalleConexion('COBIS (Sybase)', r);
      this.setStatus(detalle, 'ok');
      this.cobisStatus.set('Conectado correctamente');
      this.cobisStatusTipo.set('ok');
      this.abrirDialogo(true, 'Conectado a COBIS', detalle);
    } catch (e: any) {
      const err = this.extraerError(e, 'Error al probar COBIS');
      this.setStatus(err, 'error');
      this.cobisStatus.set(err);
      this.cobisStatusTipo.set('error');
      this.abrirDialogo(false, 'No se pudo conectar a COBIS', err);
    } finally {
      this.probandoCobis.set(false);
    }
  }

  cerrarDialogo(): void {
    this.dialogOpen.set(false);
  }

  irA(id: string, ev?: Event): void {
    ev?.preventDefault();
    const el = document.getElementById(id);
    if (el instanceof HTMLDetailsElement) el.open = true;
    queueMicrotask(() => el?.scrollIntoView({ behavior: 'smooth', block: 'start' }));
  }

  onConfirmEliminar(ok: boolean): void {
    if (ok) void this.confirmarEliminarRepo();
    else this.cancelarEliminarRepo();
  }

  async refreshModulos(): Promise<void> {
    try {
      await this.catalog.syncPruebas();
      this.modulos.set(this.catalog.listaModulosCompleta());
      const d = await this.pruebasAdmin.refreshDeshacer();
      this.puedeDeshacerModulos.set(d.disponible);
      this.deshacerHintModulos.set(d.hint);
    } catch {
      this.modulos.set(this.catalog.listaModulosCompleta());
      this.puedeDeshacerModulos.set(false);
      this.deshacerHintModulos.set('');
    }
  }

  toggleGrupoModulo(id: string): void {
    this.modulosAbiertos.update((a) => ({ ...a, [id]: !a[id] }));
  }

  toggleMenuModulo(id: string, ev: Event): void {
    ev.stopPropagation();
    this.menuModuloId.update((cur) => (cur === id ? null : id));
  }

  agregarPruebaModulo(_m: ModuloListaItem): void {
    this.menuModuloId.set(null);
    this.toast.info('Para crear pruebas usá Generar pruebas.');
    void this.router.navigate(['/generar']);
  }

  editarPruebaModulo(_p: { titulo: string }): void {
    this.toast.info('Para editar Gherkin usá Generar pruebas → Módulos.');
    void this.router.navigate(['/generar']);
  }

  async eliminarModuloModulo(m: ModuloListaItem, ev: Event): Promise<void> {
    ev.preventDefault();
    ev.stopPropagation();
    this.menuModuloId.set(null);
    const ok = await this.pruebasAdmin.eliminarModulo(
      m,
      (title, body, label, danger) => this.askConfirmPrueba(title, body, label, danger),
      (msg, tipo) => this.setStatus(msg, tipo)
    );
    if (ok) await this.refreshModulos();
  }

  async eliminarPruebaModulo(p: {
    id: string;
    generada: boolean;
    pruebaArchivo?: string;
    titulo: string;
  }): Promise<void> {
    const ok = await this.pruebasAdmin.eliminarPrueba(
      p,
      (title, body, label, danger) => this.askConfirmPrueba(title, body, label, danger),
      (msg, tipo) => this.setStatus(msg, tipo)
    );
    if (ok) await this.refreshModulos();
  }

  async deshacerModulo(): Promise<void> {
    const ok = await this.pruebasAdmin.deshacer((msg, tipo) => this.setStatus(msg, tipo));
    if (ok) await this.refreshModulos();
  }

  abrirRenameModulo(m: ModuloListaItem, ev: Event): void {
    ev.preventDefault();
    ev.stopPropagation();
    this.renameId.set(m.id);
    this.renameValue = m.nombre;
    this.modulosAbiertos.update((a) => ({ ...a, [m.id]: true }));
  }

  cancelRenameModulo(ev?: Event): void {
    ev?.preventDefault();
    ev?.stopPropagation();
    this.renameId.set(null);
    this.renameValue = '';
  }

  async confirmRenameModulo(m: ModuloListaItem, ev: Event): Promise<void> {
    ev.preventDefault();
    ev.stopPropagation();
    const r = await this.catalog.renombrarModulo(m.id, this.renameValue);
    if (!r.ok) {
      this.setStatus(r.error || 'No se pudo renombrar.', 'error');
      return;
    }
    this.renameId.set(null);
    this.setStatus('Módulo renombrado.', 'ok');
    await this.refreshModulos();
  }

  async toggleGherkinModulo(p: {
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

  askConfirmPrueba(
    title: string,
    body: string,
    confirmLabel = 'Confirmar',
    danger = false
  ): Promise<boolean> {
    this.confirmPruebaTitle.set(title);
    this.confirmPruebaBody.set(body);
    this.confirmPruebaLabel.set(confirmLabel);
    this.confirmPruebaDanger.set(danger);
    this.confirmPruebaOpen.set(true);
    return new Promise((resolve) => {
      this.confirmPruebaResolver = resolve;
    });
  }

  closeConfirmPrueba(ok: boolean): void {
    this.confirmPruebaOpen.set(false);
    const r = this.confirmPruebaResolver;
    this.confirmPruebaResolver = null;
    if (r) r(ok);
  }

  @HostListener('document:keydown.escape')
  onEsc(): void {
    if (this.confirmPruebaOpen()) this.closeConfirmPrueba(false);
    else if (this.confirmEliminarOpen()) this.cancelarEliminarRepo();
    else if (this.dialogOpen()) this.cerrarDialogo();
  }

  toggleRepo(i: number): void {
    this.repoExpandido.update((m) => ({ ...m, [i]: !m[i] }));
  }

  get healthJira(): 'ok' | 'warn' {
    return this.jiraEmail.trim() && this.jiraToken.trim() ? 'ok' : 'warn';
  }
  get healthSot(): 'ok' | 'warn' {
    return this.usuario.trim() && (this.tienePasswordLogin || !!this.passwordLogin.trim()) ? 'ok' : 'warn';
  }
  get healthSql(): 'ok' | 'warn' {
    return this.sqlSotServidor.trim() && (this.tienePasswordSqlSot || !!this.passwordSqlSot.trim())
      ? 'ok'
      : 'warn';
  }
  get healthCobis(): 'ok' | 'warn' {
    return this.cobisHost.trim() && (this.tienePasswordCobis || !!this.passwordCobis.trim()) ? 'ok' : 'warn';
  }

  hintPass(tiene: boolean): string {
    return tiene
      ? 'Ya hay una contraseña guardada. Dejá el campo vacío para mantenerla.'
      : 'Sin contraseña guardada todavía.';
  }

  async cargarCatalogo(): Promise<void> {
    try {
      const d = await firstValueFrom(this.api.getCatalogoSot());
      this.permitirGitPull = d?.config?.permitirGitPull !== false;
      this.ramaObjetivo = String(d?.config?.ramaObjetivo || 'develop').trim() || 'develop';
      this.tfsUsuario = String(d?.config?.tfsUsuario || '').trim();
      this.tienePatTfs = !!d?.config?.tienePatTfs;
      this.tfsPat = '';
      const reposCfg = d?.config?.repos || [];
      this.catalogoRepos = (reposCfg.length ? reposCfg : []).map((r: any) => ({
        id: r.id || '',
        nombre: r.nombre || r.id || '',
        rol: r.rol || '',
        urlRemota: r.urlRemota || '',
        rutaEnmascarada: r.rutaEnmascarada || '',
        usaCache: !!r.usaCache,
        estado: r.estado || 'pendiente',
        existe: r.existe,
        rama: r.rama,
        ramaObjetivo: r.ramaObjetivo || this.ramaObjetivo,
        commitCorto: r.commitCorto,
        atrasadoVsObjetivo: r.atrasadoVsObjetivo,
        okPull: r.okPull,
        aviso: r.aviso,
        conteos: r.conteos
      }));
      this.catalogoGenerado.set(d?.catalogo?.generadoUtc ? String(d.catalogo.generadoUtc) : '');
      this.catalogoResumen.set(d?.catalogo?.resumenCorto ? String(d.catalogo.resumenCorto) : '');
    } catch {
      this.catalogoResumen.set('No se pudo leer el catálogo (¿Runner offline?).');
    }
  }

  etiquetaEstado(repo: { estado?: string; existe?: boolean }): string {
    switch (repo.estado) {
      case 'ok':
        return 'Sync OK';
      case 'error':
        return 'Error sync';
      case 'indexado':
        return 'Indexado';
      case 'clonado':
        return 'Clonado';
      case 'pendiente':
      default:
        return repo.existe === false ? 'Pendiente clone' : 'Pendiente';
    }
  }

  agregarRepo(): void {
    this.catalogoRepos = [
      ...this.catalogoRepos,
      {
        id: '',
        nombre: '',
        rol: '',
        urlRemota: '',
        rutaEnmascarada: '***…/cache/?',
        usaCache: true,
        estado: 'pendiente'
      }
    ];
  }

  pedirEliminarRepo(index: number): void {
    const repo = this.catalogoRepos[index];
    if (!repo) return;
    this.confirmEliminarIndex.set(index);
    this.confirmEliminarNombre.set(repo.nombre || repo.id || `repositorio #${index + 1}`);
    this.confirmEliminarOpen.set(true);
  }

  cancelarEliminarRepo(): void {
    this.confirmEliminarOpen.set(false);
    this.confirmEliminarIndex.set(-1);
    this.confirmEliminarNombre.set('');
  }

  async confirmarEliminarRepo(): Promise<void> {
    const i = this.confirmEliminarIndex();
    this.cancelarEliminarRepo();
    if (i < 0 || i >= this.catalogoRepos.length) return;
    this.catalogoRepos = this.catalogoRepos.filter((_, idx) => idx !== i);
    try {
      await this.guardarRutasCatalogoSilent();
      this.setStatus('Repositorio eliminado del catálogo.', 'ok');
      await this.cargarCatalogo();
    } catch (e: any) {
      this.setStatus(this.extraerError(e, 'No se pudo guardar tras eliminar'), 'error');
    }
  }

  private bodyCatalogoConfig(): Record<string, unknown> {
    return {
      permitirGitPull: this.permitirGitPull,
      ramaObjetivo: this.ramaObjetivo.trim() || 'develop',
      tfsUsuario: this.tfsUsuario.trim(),
      tfsPat: this.tfsPat.trim(),
      // Sin ruta real: el servidor conserva la existente o usa cache.
      repos: this.catalogoRepos.map((r) => ({
        id: r.id,
        nombre: r.nombre,
        rol: r.rol,
        urlRemota: (r.urlRemota || '').trim() || null
      }))
    };
  }

  async guardarRutasCatalogo(): Promise<void> {
    try {
      const r = await firstValueFrom(this.api.postCatalogoSotConfig(this.bodyCatalogoConfig()));
      if (r?.ok === false) {
        this.setStatus(r.error || 'No se pudieron guardar los repos.', 'error');
        return;
      }
      this.tfsPat = '';
      if (typeof r?.tienePatTfs === 'boolean') this.tienePatTfs = r.tienePatTfs;
      this.setStatus(r?.mensaje || 'Repos del catálogo guardados.', 'ok');
      await this.cargarCatalogo();
    } catch (e: any) {
      this.setStatus(this.extraerError(e, 'Error al guardar repos del catálogo'), 'error');
    }
  }

  async sincronizarCatalogo(pull = true): Promise<void> {
    if (this.syncingCatalogo()) return;
    this.syncingCatalogo.set(true);
    this.setStatus(pull ? 'Sincronizando repos (git pull) e indexando…' : 'Indexando repos…', 'info');
    try {
      await this.guardarRutasCatalogoSilent();
      const r = await firstValueFrom(this.api.syncCatalogoSot(pull));
      if (r?.ok === false) {
        this.setStatus(r.error || 'Falló la sincronización.', 'error');
        this.abrirDialogo(false, 'Catálogo célula SOT', r.error || 'Falló la sincronización.');
        return;
      }
      const detalle = [
        r.mensaje || 'Catálogo actualizado.',
        r.resumenCorto || '',
        r.generadoUtc ? `Generado: ${r.generadoUtc}` : ''
      ]
        .filter(Boolean)
        .join('\n');
      this.setStatus(detalle, 'ok');
      this.abrirDialogo(true, 'Catálogo célula SOT actualizado', detalle);
      await this.cargarCatalogo();
    } catch (e: any) {
      const err = this.extraerError(e, 'Error al sincronizar catálogo');
      this.setStatus(err, 'error');
      this.abrirDialogo(false, 'Catálogo célula SOT', err);
    } finally {
      this.syncingCatalogo.set(false);
    }
  }

  private async guardarRutasCatalogoSilent(): Promise<void> {
    const r = await firstValueFrom(this.api.postCatalogoSotConfig(this.bodyCatalogoConfig()));
    this.tfsPat = '';
    if (typeof r?.tienePatTfs === 'boolean') this.tienePatTfs = r.tienePatTfs;
  }

  private setStatus(msg: string, tipo: string): void {
    this.status.set(msg);
    this.statusTipo.set(tipo);
  }

  private abrirDialogo(ok: boolean, titulo: string, cuerpo: string): void {
    this.dialogOk.set(ok);
    this.dialogTitle.set(titulo);
    this.dialogBody.set(cuerpo);
    this.dialogOpen.set(true);
  }

  private armarDetalleConexion(
    etiqueta: string,
    r: { mensaje?: string; motor?: string; baseDatos?: string; servidor?: string }
  ): string {
    const lineas = [
      r.mensaje || `Conexión a ${etiqueta} correcta.`,
      r.motor ? `Motor: ${r.motor}` : '',
      r.servidor ? `Servidor: ${r.servidor}` : '',
      r.baseDatos ? `Base de datos: ${r.baseDatos}` : ''
    ].filter(Boolean);
    return lineas.join('\n');
  }

  private extraerError(e: any, fallback: string): string {
    const body = e?.error;
    if (typeof body === 'string' && body.trim()) return body;
    if (body?.error) return String(body.error);
    if (body?.mensaje) return String(body.mensaje);
    if (e?.message && !String(e.message).startsWith('Http failure')) return String(e.message);
    if (e?.status) return `${fallback} (HTTP ${e.status}). Revisá que el Runner esté en marcha.`;
    return fallback;
  }
}
