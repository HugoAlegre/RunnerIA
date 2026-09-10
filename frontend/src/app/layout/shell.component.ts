import { Component, OnDestroy, OnInit, effect, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../core/services/api.service';
import { ThemeService } from '../core/services/theme.service';
import { CatalogService } from '../core/services/catalog.service';
import { RunService } from '../core/services/run.service';
import { RUNNER_VERSION_LABEL } from '../core/runner-version';
import { ToastService } from '../core/services/toast.service';
import { getRunnerAuthToken, setRunnerAuthToken } from '../core/auth.interceptor';
import { AuthSessionService } from '../core/services/auth-session.service';
import { PasswordFieldComponent } from '../shared/password-field/password-field.component';
import { ConfirmDialogComponent } from '../shared/confirm-dialog/confirm-dialog.component';
import { WorkspaceProfileService } from '../core/services/workspace-profile.service';
import {
  INTEGRACION_TIPOS,
  ProyectoIntegracion,
  integracionesGenericasPorDefecto
} from '../core/models/proyecto-integracion';
import { ProyectoPlantilla, RunnerProyecto } from '../core/models/runner-proyecto';

type AuthModo = 'login' | 'setup' | 'forceChange' | 'forgot' | 'perfil';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    FormsModule,
    PasswordFieldComponent,
    ConfirmDialogComponent
  ],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss'
})
export class ShellComponent implements OnInit, OnDestroy {
  readonly api = inject(ApiService);
  readonly theme = inject(ThemeService);
  readonly catalog = inject(CatalogService);
  readonly runs = inject(RunService);
  readonly toast = inject(ToastService);
  readonly workspace = inject(WorkspaceProfileService);
  private readonly authSession = inject(AuthSessionService);
  private readonly router = inject(Router);

  subtitulo = 'Elegí un módulo, abrí una prueba y ejecutá.';
  readonly runnerVersionLabel = RUNNER_VERSION_LABEL;
  iaNombre = signal('RunnerIA');
  proyectoNombre = signal('');
  /** Evita flash de login/proyecto o Runner antes de verificar sesión con el servidor. */
  authResuelto = signal(false);
  authBloqueado = signal(true);
  pinConfigurado = signal(false);
  pinPredeterminado = signal('1234');
  pinVencimientoHabilitado = signal(false);
  authBusy = signal(false);
  perfilBusy = signal(false);
  authError = signal('');
  authInfo = signal('');
  authModo = signal<AuthModo>('login');
  preflightOk = signal(false);
  preflightHint = signal('');
  recoveryEmailMascara = signal('');
  /** Lista de proyectos RunnerIA al ingresar. */
  proyectos = signal<RunnerProyecto[]>([]);
  proyectoSeleccionadoId = signal('');
  proyectoFormModo = signal<'nuevo' | 'editar' | null>(null);
  proyectoFormNombre = '';
  proyectoFormDescripcion = '';
  proyectoFormId = '';
  proyectoFormIaNombre = 'RunnerIA';
  proyectoFormPlantilla: ProyectoPlantilla = 'generico';
  proyectoFormIntegraciones: ProyectoIntegracion[] = integracionesGenericasPorDefecto();
  readonly integracionTipos = INTEGRACION_TIPOS;
  eliminarProyectoDialog = signal(false);
  pinInput = '';
  pinConfirmInput = '';
  pinActualInput = '';
  pinNuevoInput = '';
  codigoRecuperacion = '';
  recoveryEmailInput = '';
  private healthTimer: ReturnType<typeof setInterval> | null = null;
  private authInicialHecho = false;

  constructor() {
    effect(() => {
      if (this.authSession.requiereLogin()) {
        this.authModo.set('login');
        this.authBloqueado.set(true);
        this.authInfo.set('La sesión expiró (servidor reiniciado). Ingresá el PIN otra vez.');
      }
    });
  }

  ngOnInit(): void {
    void this.refresh();
    this.healthTimer = setInterval(() => void this.refresh(), 10000);
  }

  ngOnDestroy(): void {
    if (this.healthTimer) clearInterval(this.healthTimer);
  }

  async refresh(): Promise<void> {
    const ok = await this.api.health();
    if (!ok) {
      this.preflightOk.set(false);
      this.preflightHint.set('');
      this.finalizarAuthInicial();
      return;
    }
    await this.cargarPreflight();
    await this.verificarAuth();
    if (!this.authBloqueado()) {
      await this.cargarRunnerIa();
      await this.catalog.syncPruebas();
    }
  }

  async verificarAuth(): Promise<void> {
    try {
      const s = await firstValueFrom(this.api.authStatus());
      this.pinConfigurado.set(!!s?.pinConfigurado);
      if (s?.pinPredeterminado) this.pinPredeterminado.set(s.pinPredeterminado);
      this.pinVencimientoHabilitado.set(!!s?.pinVencimientoHabilitado);
      this.recoveryEmailMascara.set(s?.recoveryEmailMascara || '');
      const token = getRunnerAuthToken();

      if (!s?.pinConfigurado) {
        this.bloquearAuth('setup');
        if (!this.pinInput) this.pinInput = this.pinPredeterminado();
        if (!this.pinConfirmInput) this.pinConfirmInput = this.pinPredeterminado();
        return;
      }
      if (!s?.autenticado && !token) {
        this.bloquearAuth('login');
        return;
      }
      if (token && s?.autenticado === false) {
        setRunnerAuthToken('');
        this.bloquearAuth('login');
        this.authSession.marcarSesionExpirada();
        return;
      }
      if (s?.pinVencido && s?.pinVencimientoHabilitado) {
        this.bloquearAuth('forceChange');
        return;
      }
      if (!this.authSession.proyectoGateOk()) {
        this.bloquearAuth('perfil');
        void this.cargarProyectosLista();
        return;
      }
      this.authBloqueado.set(false);
      this.authModo.set('login');
    } catch {
      if (!this.authSession.proyectoGateOk()) {
        this.authBloqueado.set(true);
        this.authModo.set(getRunnerAuthToken() ? 'perfil' : 'login');
        void this.cargarProyectosLista();
      }
      this.authError.set('No se pudo verificar la sesión con el servidor.');
    } finally {
      this.finalizarAuthInicial();
    }
  }

  async cargarPreflight(): Promise<void> {
    try {
      const p = await firstValueFrom(this.api.getPreflight());
      this.preflightOk.set(!!p?.ok);
      if (p?.automatizacionOk === false || p?.modoAyuda) {
        this.preflightHint.set('Sin AutomatizacionSOT');
      } else if (!p?.secretsPresent) {
        this.preflightHint.set('Sin appsettings.secrets.json');
      } else if (!p?.playwrightOk) {
        this.preflightHint.set('Playwright no instalado');
      } else {
        this.preflightHint.set('');
      }
    } catch {
      this.preflightOk.set(false);
      this.preflightHint.set('');
    }
  }

  async cargarRunnerIa(): Promise<void> {
    try {
      const d = await firstValueFrom(this.api.getRunnerIaProyecto());
      const activo = d?.proyectoActivo;
      this.proyectoNombre.set(activo?.nombre || 'Automatización');
      this.iaNombre.set(activo?.iaNombre || 'RunnerIA');
    } catch {
      this.proyectoNombre.set(this.workspace.proyectoId() || 'Automatización');
      this.iaNombre.set('RunnerIA');
    }
  }

  async cargarProyectosLista(): Promise<void> {
    try {
      const d = await firstValueFrom(this.api.getRunnerIaProyecto());
      const lista = (d?.proyectos || []) as RunnerProyecto[];
      this.proyectos.set(lista);
      const activoId =
        d?.proyectoActivoId ||
        d?.proyectoActivo?.id ||
        this.workspace.proyectoId() ||
        lista[0]?.id ||
        '';
      if (activoId && lista.some((p) => p.id === activoId)) {
        this.proyectoSeleccionadoId.set(activoId);
      } else if (lista[0]?.id) {
        this.proyectoSeleccionadoId.set(lista[0].id);
      }
    } catch {
      this.authError.set('No se pudo cargar la lista de proyectos.');
    }
  }

  seleccionarProyecto(id: string): void {
    this.proyectoSeleccionadoId.set(id);
    this.limpiarMensajesAuth();
  }

  abrirFormNuevoProyecto(): void {
    this.proyectoFormModo.set('nuevo');
    this.proyectoFormId = '';
    this.proyectoFormNombre = '';
    this.proyectoFormDescripcion = '';
    this.proyectoFormIaNombre = 'RunnerIA';
    this.proyectoFormPlantilla = 'generico';
    this.proyectoFormIntegraciones = integracionesGenericasPorDefecto();
    this.limpiarMensajesAuth();
  }

  abrirFormEditarProyecto(): void {
    const id = this.proyectoSeleccionadoId();
    const p = this.proyectos().find((x) => x.id === id);
    if (!p) {
      this.authError.set('Seleccioná un proyecto para modificar.');
      return;
    }
    this.proyectoFormModo.set('editar');
    this.proyectoFormId = p.id;
    this.proyectoFormNombre = p.nombre || '';
    this.proyectoFormDescripcion = p.descripcion || '';
    this.proyectoFormIaNombre = p.iaNombre || 'RunnerIA';
    this.proyectoFormPlantilla = p.plantilla === 'sot' ? 'sot' : 'generico';
    this.proyectoFormIntegraciones =
      p.integraciones?.length ? p.integraciones.map((x) => ({ ...x })) : integracionesGenericasPorDefecto();
    this.limpiarMensajesAuth();
  }

  agregarIntegracionProyecto(): void {
    const n = this.proyectoFormIntegraciones.length + 1;
    this.proyectoFormIntegraciones = [
      ...this.proyectoFormIntegraciones,
      { id: `int-${n}`, tipo: 'otro', etiqueta: 'Integración', url: '' }
    ];
  }

  quitarIntegracionProyecto(idx: number): void {
    this.proyectoFormIntegraciones = this.proyectoFormIntegraciones.filter((_, i) => i !== idx);
  }

  esPlantillaSotForm(): boolean {
    return this.proyectoFormPlantilla === 'sot';
  }

  cancelarFormProyecto(): void {
    this.proyectoFormModo.set(null);
  }

  async guardarFormProyecto(): Promise<void> {
    if (this.perfilBusy()) return;
    const nombre = this.proyectoFormNombre.trim();
    if (!nombre) {
      this.authError.set('El nombre del proyecto es obligatorio.');
      return;
    }
    this.perfilBusy.set(true);
    this.limpiarMensajesAuth();
    try {
      const modo = this.proyectoFormModo();
      let r;
      const integraciones =
        this.proyectoFormPlantilla === 'generico' ? this.proyectoFormIntegraciones : undefined;
      const payloadBase = {
        nombre,
        descripcion: this.proyectoFormDescripcion.trim(),
        iaNombre: this.proyectoFormIaNombre.trim() || 'RunnerIA',
        plantilla: this.proyectoFormPlantilla,
        integraciones
      };
      if (modo === 'nuevo') {
        r = await firstValueFrom(
          this.api.postRunnerIaProyecto({
            agregar: {
              id: this.proyectoFormId.trim() || undefined,
              ...payloadBase
            }
          })
        );
      } else if (modo === 'editar') {
        r = await firstValueFrom(
          this.api.postRunnerIaProyecto({
            modificar: {
              id: this.proyectoFormId,
              ...payloadBase
            }
          })
        );
      } else {
        return;
      }
      if (r?.ok === false) {
        this.authError.set(r.error || 'No se pudo guardar el proyecto.');
        return;
      }
      this.proyectoFormModo.set(null);
      await this.cargarProyectosLista();
      const nuevoId = r?.proyectoActivoId || r?.proyectoActivo?.id;
      if (nuevoId) this.proyectoSeleccionadoId.set(nuevoId);
      this.toast.ok(modo === 'nuevo' ? 'Proyecto creado.' : 'Proyecto actualizado.');
    } catch (e: any) {
      this.authError.set(e?.error?.error || 'Error al guardar el proyecto.');
    } finally {
      this.perfilBusy.set(false);
    }
  }

  pedirEliminarProyecto(): void {
    const id = this.proyectoSeleccionadoId();
    if (!id) {
      this.authError.set('Seleccioná un proyecto para eliminar.');
      return;
    }
    if (this.proyectos().length <= 1) {
      this.authError.set('No se puede eliminar el único proyecto.');
      return;
    }
    this.eliminarProyectoDialog.set(true);
  }

  eliminarProyectoBody(): string {
    const id = this.proyectoSeleccionadoId();
    const p = this.proyectos().find((x) => x.id === id);
    const nom = p?.nombre || id || '';
    return `¿Eliminar el proyecto «${nom}»? No borra los .feature del disco.`;
  }

  async confirmarEliminarProyecto(ok: boolean): Promise<void> {
    this.eliminarProyectoDialog.set(false);
    if (!ok) return;
    const id = this.proyectoSeleccionadoId();
    if (!id) return;
    if (this.perfilBusy()) return;
    this.perfilBusy.set(true);
    this.limpiarMensajesAuth();
    try {
      const r = await firstValueFrom(this.api.postRunnerIaProyecto({ eliminar: id }));
      if (r?.ok === false) {
        this.authError.set(r.error || 'No se pudo eliminar.');
        return;
      }
      await this.cargarProyectosLista();
      const nextId = r?.proyectoActivoId || this.proyectos()[0]?.id || '';
      if (nextId) this.proyectoSeleccionadoId.set(nextId);
      this.toast.ok('Proyecto eliminado.');
    } catch (e: any) {
      this.authError.set(e?.error?.error || 'Error al eliminar.');
    } finally {
      this.perfilBusy.set(false);
    }
  }

  async entrarConProyecto(): Promise<void> {
    const id = this.proyectoSeleccionadoId();
    if (!id) {
      this.authError.set('Seleccioná un proyecto o creá uno nuevo.');
      return;
    }
    if (this.perfilBusy()) return;
    this.perfilBusy.set(true);
    this.limpiarMensajesAuth();
    try {
      const r = await firstValueFrom(this.api.postRunnerIaProyecto({ proyectoActivoId: id }));
      if (r?.ok === false) {
        this.authError.set(r.error || 'No se pudo activar el proyecto.');
        return;
      }
      this.workspace.elegirProyecto(id, r?.proyectoActivo?.plantilla ?? null);
      this.proyectoNombre.set(r?.proyectoActivo?.nombre || id);
      this.iaNombre.set(r?.proyectoActivo?.iaNombre || 'RunnerIA');
      await this.catalog.syncPruebas();
      this.authBloqueado.set(false);
      this.authModo.set('login');
      this.authSession.limpiarRequiereLogin();
      this.authSession.marcarProyectoGateOk();
      await this.router.navigate(['/runner']);
    } catch {
      this.authError.set('No se pudo cargar el catálogo. Revisá que el servidor esté en marcha.');
    } finally {
      this.perfilBusy.set(false);
    }
  }

  async enviarAuth(): Promise<void> {
    if (this.authBusy()) return;
    this.authBusy.set(true);
    this.limpiarMensajesAuth();
    try {
      const pin = this.pinInput.trim();
      if (pin.length < 4) {
        this.authError.set('PIN mínimo 4 caracteres.');
        return;
      }
      if (!this.pinConfigurado()) {
        const err = this.errorPinNuevo(pin, this.pinConfirmInput.trim());
        if (err) {
          this.authError.set(err);
          return;
        }
      }
      const r = this.pinConfigurado()
        ? await firstValueFrom(this.api.authLogin(pin))
        : await firstValueFrom(this.api.authSetup(pin, this.pinConfirmInput.trim() || pin));
      if (r?.ok === false || !r?.token) {
        this.authError.set(r?.error || 'No se pudo autenticar.');
        return;
      }
      setRunnerAuthToken(r.token);
      this.pinInput = '';
      this.pinConfirmInput = '';
      this.pinConfigurado.set(true);
      if (r.debeCambiarPin) {
        this.bloquearAuth('forceChange');
        this.authInfo.set(r.mensaje || 'Debés cambiar el PIN (caduca cada 3 meses).');
        return;
      }
      this.authSession.limpiarProyectoGate();
      this.bloquearAuth('perfil');
      void this.cargarProyectosLista();
    } catch (e: any) {
      this.authError.set(e?.error?.error || 'Error de autenticación.');
    } finally {
      this.authBusy.set(false);
    }
  }

  abrirOlvido(): void {
    this.limpiarMensajesAuth();
    this.codigoRecuperacion = '';
    this.pinNuevoInput = '';
    this.pinConfirmInput = '';
    this.bloquearAuth('forgot');
  }

  volverLogin(): void {
    this.limpiarMensajesAuth();
    this.authModo.set(this.pinConfigurado() ? 'login' : 'setup');
  }

  async enviarClaveRecuperacion(): Promise<void> {
    if (this.authBusy()) return;
    this.authBusy.set(true);
    this.limpiarMensajesAuth();
    try {
      const r = await firstValueFrom(this.api.authRecuperarEnviar(this.recoveryEmailInput.trim()));
      if (r?.ok === false) {
        this.authError.set(r.error || 'No se pudo enviar la clave.');
        return;
      }
      this.authInfo.set(r.mensaje || 'Clave enviada.');
    } catch (e: any) {
      this.authError.set(e?.error?.error || 'Error al enviar la clave.');
    } finally {
      this.authBusy.set(false);
    }
  }

  async restablecerConClave(): Promise<void> {
    if (this.authBusy()) return;
    this.authBusy.set(true);
    this.authError.set('');
    try {
      const codigo = this.codigoRecuperacion.trim();
      const nuevo = this.pinNuevoInput.trim();
      if (!codigo) {
        this.authError.set('Ingresá la clave del email.');
        return;
      }
      const err = this.errorPinNuevo(nuevo, this.pinConfirmInput.trim());
      if (err) {
        this.authError.set(err);
        return;
      }
      const r = await firstValueFrom(
        this.api.authRecuperarRestablecer(codigo, nuevo, this.pinConfirmInput.trim())
      );
      if (r?.ok === false) {
        this.authError.set(r.error || 'No se pudo restablecer.');
        return;
      }
      setRunnerAuthToken('');
      this.authInfo.set(r.mensaje || 'PIN restablecido. Iniciá sesión.');
      this.codigoRecuperacion = '';
      this.pinNuevoInput = '';
      this.pinConfirmInput = '';
      this.authModo.set('login');
    } catch (e: any) {
      this.authError.set(e?.error?.error || 'Error al restablecer.');
    } finally {
      this.authBusy.set(false);
    }
  }

  async cambiarPinForzado(): Promise<void> {
    if (this.authBusy()) return;
    this.authBusy.set(true);
    this.authError.set('');
    try {
      const actual = this.pinActualInput.trim();
      const nuevo = this.pinNuevoInput.trim();
      const conf = this.pinConfirmInput.trim();
      if (actual.length < 4) {
        this.authError.set('PIN mínimo 4 caracteres.');
        return;
      }
      const err = this.errorPinNuevo(nuevo, conf);
      if (err) {
        this.authError.set(err);
        return;
      }
      const r = await firstValueFrom(this.api.authCambiarPin(actual, nuevo, conf));
      if (r?.ok === false) {
        this.authError.set(r.error || 'No se pudo cambiar el PIN.');
        return;
      }
      setRunnerAuthToken('');
      this.pinActualInput = '';
      this.pinNuevoInput = '';
      this.pinConfirmInput = '';
      this.authInfo.set(r.mensaje || 'PIN actualizado. Iniciá sesión.');
      this.bloquearAuth('login');
    } catch (e: any) {
      this.authError.set(e?.error?.error || 'Error al cambiar PIN.');
    } finally {
      this.authBusy.set(false);
    }
  }

  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.api.authLogout());
    } catch {
      /* ignore */
    }
    setRunnerAuthToken('');
    this.authSession.limpiarProyectoGate();
    this.authModo.set('login');
    this.authBloqueado.set(this.pinConfigurado());
  }

  setSub(ruta: string): void {
    const key = ruta.replace(/^\//, '');
    const map: Record<string, string> = {
      runner: 'Elegí un módulo, abrí una prueba y ejecutá.',
      generar: 'Analizá tickets, generá Gherkin y administrá módulos de testing.',
      config: 'Perfil SOT/otro, URL, bases, Jira/Xray y repos.',
      ayuda: 'Ayuda y manual de uso del Runner.'
    };
    this.subtitulo = map[key] || this.subtitulo;
  }

  private finalizarAuthInicial(): void {
    if (this.authInicialHecho) return;
    this.authInicialHecho = true;
    this.authResuelto.set(true);
  }

  private bloquearAuth(modo: AuthModo): void {
    this.authModo.set(modo);
    this.authBloqueado.set(true);
  }

  private limpiarMensajesAuth(): void {
    this.authError.set('');
    this.authInfo.set('');
  }

  private errorPinNuevo(nuevo: string, conf: string): string | null {
    if (nuevo.length < 4) return 'PIN nuevo mínimo 4 caracteres.';
    if (nuevo !== conf) return 'PIN nuevo y confirmación no coinciden.';
    return null;
  }
}
