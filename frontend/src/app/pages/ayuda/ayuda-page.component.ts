import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { PageTocComponent, PageTocItem } from '../../shared/page-toc/page-toc.component';
import { WorkspaceProfileService } from '../../core/services/workspace-profile.service';
import { RUNNER_VERSION_LABEL } from '../../core/runner-version';

interface TocItem {
  id: string;
  label: string;
  level: number;
}

@Component({
  selector: 'app-ayuda-page',
  standalone: true,
  imports: [CommonModule, PageTocComponent],
  templateUrl: './ayuda-page.component.html',
  styleUrl: './ayuda-page.component.scss'
})
export class AyudaPageComponent implements OnInit {
  private api = inject(ApiService);
  private sanitizer = inject(DomSanitizer);
  readonly workspace = inject(WorkspaceProfileService);
  readonly runnerVersion = RUNNER_VERSION_LABEL;

  manualHtml = signal<SafeHtml>(this.sanitizer.bypassSecurityTrustHtml('<p class="muted">Cargando manual…</p>'));
  toc = signal<TocItem[]>([]);
  status = signal('');
  /** Manual técnico MANUAL.md colapsado por defecto (guía UI arriba). */
  manualExpandido = signal(false);

  private readonly tocSot: PageTocItem[] = [
    { id: 'ayuda-inicio', label: 'Empezar en 5 min' },
    { id: 'ayuda-mapa', label: 'Mapa de la app' },
    { id: 'ayuda-proyecto-perfil', label: 'Proyecto vs perfil' },
    { id: 'ayuda-config', label: 'Configuración' },
    { id: 'ayuda-ejecutar', label: 'Ejecutar pruebas' },
    { id: 'ayuda-evidencias', label: 'Evidencias e informes' },
    { id: 'ayuda-release9', label: 'Release 9' },
    { id: 'ayuda-generar', label: 'Generar casos' },
    { id: 'ayuda-runneria', label: 'RunnerIA' },
    { id: 'ayuda-jira', label: 'Jira y Xray' },
    { id: 'ayuda-qa', label: 'QA / Auth OpenID' },
    { id: 'ayuda-catalogo', label: 'Catálogo / repos' },
    { id: 'ayuda-problemas', label: 'Problemas frecuentes' },
    { id: 'ayuda-tecnologias', label: 'Tecnologías' },
    { id: 'ayuda-llm', label: 'LLM opcional' },
    { id: 'ayuda-manual', label: 'Manual técnico' }
  ];

  private readonly tocGenerico: PageTocItem[] = [
    { id: 'ayuda-inicio', label: 'Inicio' },
    { id: 'ayuda-mapa', label: 'Mapa de la app' },
    { id: 'ayuda-config', label: 'Configuración' },
    { id: 'ayuda-ejecutar', label: 'Ejecutar pruebas' },
    { id: 'ayuda-evidencias', label: 'Evidencias' },
    { id: 'ayuda-jira', label: 'Integraciones' },
    { id: 'ayuda-problemas', label: 'Problemas frecuentes' },
    { id: 'ayuda-tecnologias', label: 'Tecnologías' }
  ];

  tocItems = computed<PageTocItem[]>(() => {
    const base = this.workspace.esSotAccusys() ? this.tocSot : this.tocGenerico;
    if (!this.workspace.esSotAccusys() || !this.manualExpandido()) return base;
    const manual = this.toc();
    if (!manual.length) return base;
    return [
      ...base,
      ...manual.map((t, i) => ({
        id: t.id,
        label: t.label,
        level: (t.level === 2 ? 2 : 1) as 1 | 2,
        sepBefore: i === 0 ? 'Secciones MANUAL.md' : undefined
      }))
    ];
  });

  readonly criteriosGenerar = [
    {
      campo: 'Link Jira',
      obligatorio: 'No',
      criterio: 'URL Atlassian válida (ej. …/browse/SC-161). Requiere email + API token en Configuración.'
    },
    {
      campo: 'Nombre de la prueba',
      obligatorio: 'No',
      criterio: 'Si está vacío, se propone desde Jira o documentación al Analizar.'
    },
    {
      campo: 'Detalle',
      obligatorio: 'Sí (sin Jira)',
      criterio: 'Qué validar, en lenguaje natural: pantallas, pasos, COBIS/SQL si aplica. Si hay Detalle concreto, manda sobre plantillas genéricas.'
    },
    {
      campo: 'Notas extra',
      obligatorio: 'No',
      criterio: 'Importes, excepciones, datos puntuales que no van en Detalle.'
    },
    {
      campo: 'Archivos',
      obligatorio: 'No',
      criterio:
        'Arrastrar o elegir. .feature → vista previa directa. Docs: .pdf · .docx · .txt · .md · .json · .csv · .log · imágenes. Máx. 20 docs, 15 MB c/u. No .doc / Excel / ZIP.'
    },
    {
      campo: 'Módulo destino',
      obligatorio: 'No',
      criterio: 'Automático (desde ticket), módulo existente (suite / testing) o crear uno nuevo.'
    }
  ];

  readonly tiposCorrida = [
    {
      accion: 'Ejecutar (un caso)',
      donde: 'Panel derecho del escenario seleccionado',
      queCorre: 'Solo ese escenario Gherkin',
      evidencia: 'ZIP con título + fecha/hora de ese caso'
    },
    {
      accion: 'Regresión del módulo',
      donde: 'Último ítem de la lista del módulo activo',
      queCorre: 'Todas las pruebas del módulo seleccionado',
      evidencia: 'Informe + Excel/ZIP QA del módulo (formato SC-514)'
    },
    {
      accion: 'Correr todo (N)',
      donde: 'Botón al pie del sidebar Runner',
      queCorre: 'Regresión completa (catálogo regresión-pruebas)',
      evidencia: 'Informe unificado; distinto del lote Release 9'
    },
    {
      accion: 'Release 9',
      donde: 'Módulo Release 9 o «Correr todo Release 9»',
      queCorre: 'Solo tickets automatizados R9 (no manuales T.O./TimeOut)',
      evidencia: 'ZIP QA por ticket según configuración del módulo'
    }
  ];

  readonly release9Auto = [
    'SC-161', 'SC-402', 'SC-414', 'SC-461', 'SC-471', 'SC-472', 'SC-476',
    'SC-484', 'SC-485', 'SC-492', 'SC-493'
  ];

  readonly release9Manual = [
    { id: 'SC-466', nota: 'Error TA cierre/eliminación — Excel manual' },
    { id: 'SC-527', nota: 'Error eliminar cierre (resumen saldo) — Excel manual' },
    { id: 'SC-473–474, SC-482, SC-487', nota: 'TimeOut — fuera del Runner' },
    { id: 'SC-488–490, SC-497, SC-526', nota: 'T.O. — fuera del Runner' }
  ];

  readonly problemasFrecuentes = [
    {
      sintoma: 'Pill Offline en el brandbar',
      causa: 'API del Runner no responde (localhost:5050)',
      solucion: 'Doble clic en Iniciar-Runner.bat y no cerrar la ventana negra.'
    },
    {
      sintoma: 'Corrida termina en menos de 10 s con OK',
      causa: '0 tests ejecutados (corrida vacía u omitida)',
      solucion: 'Usar Comprobar instalación (módulo Verificar instalación); revisar tags Gherkin y build. El Runner marca «Sin pruebas».'
    },
    {
      sintoma: 'Hay una corrida en curso',
      causa: 'Solo una ejecución a la vez',
      solucion: 'Esperar, Detener, o Liberar corrida si quedó bloqueado el estado local.'
    },
    {
      sintoma: 'Jira no lee el ticket',
      causa: 'Sin email/token o token incorrecto',
      solucion: 'Configuración → Jira → Probar conexión. Token en id.atlassian.com (no es el de GitHub/Cursor).'
    },
    {
      sintoma: 'Cierre falla: aún no se registró la apertura (2609986)',
      causa: 'Caja abierta en SOT sin período coherente en remesas',
      solucion: 'Cerrar estado huérfano y reabrir caja por flujo normal (UI), no solo insert en SQL.'
    },
    {
      sintoma: 'PIN caducado',
      causa: 'Más de 3 meses desde el último cambio',
      solucion: 'Pantalla de cambio forzado de PIN o recuperación por email configurado.'
    },
    {
      sintoma: 'QA sin login / Auth',
      causa: 'OpenID no autorizado en el navegador',
      solucion: 'Configuración → QA → abrir URL Auth, aceptar certificado y permiso; Guardar.'
    }
  ];

  readonly reglasSotOperativas = [
    { regla: 'Correlativo de caja', detalle: 'Solo dígitos, máximo 4 caracteres (1–9999). No letras ni 5+ dígitos.' },
    { regla: 'Apertura de caja', detalle: 'Siempre por flujo normal (UI). Debe quedar abierta en SOT y en remesas (re_cierre).' },
    { regla: 'Fecha de proceso QA', detalle: 'Suele ser fija (ej. 2018-03-06). Es esperada del ambiente; no «corregir» sin pedido.' },
    { regla: 'Cajas de prueba', detalle: 'Usar correlativos dedicados (4371, 4161…). Evitar reutilizar cajas operativas (1, 8800…).' }
  ];

  readonly iaTecnologias = [
    {
      nombre: 'Motor local RunnerIA',
      uso: 'Heurísticas C# + plantillas por dominio + mapa de pantallas (XPath, steps, circuitos, COBIS/SQL). Funciona sin API key.'
    },
    {
      nombre: 'Mapa de automatización',
      uso: 'Índice entrenado desde la suite SpecFlow, appsettings y app-cashier. Se reentrena desde Generar → RunnerIA.'
    },
    {
      nombre: 'OCR Windows (WinRT)',
      uso: 'Lee texto de capturas al Analizar cuando no hay (o falla) la visión LLM.'
    },
    {
      nombre: 'LLM OpenAI-compatible (opcional)',
      uso: 'API chat/completions (p. ej. gpt-4o-mini). Pule Gherkin y describe imágenes. Key solo en el navegador / request.'
    },
    {
      nombre: 'Gherkin / SpecFlow + stubs',
      uso: 'Salida: .feature en Features/_pruebas. Si falta un step, al Guardar se crea stub en PruebaAutoSteps.cs.'
    },
    {
      nombre: 'Memoria de ejemplos',
      uso: 'aprendizaje.json: ejemplos al Guardar para reutilizar circuitos en análisis futuros.'
    }
  ];

  readonly tecnologias = [
    { nombre: '.NET 9 / C#', uso: 'Proyecto de automatización y API del Runner.' },
    { nombre: 'Angular (TypeScript)', uso: 'Interfaz del Runner (Runner, Generar, Configuración, Ayuda).' },
    { nombre: 'ASP.NET Minimal API', uso: 'API local en localhost:5050 (ejecutar, config, generar pruebas).' },
    { nombre: 'Gherkin / SpecFlow', uso: 'Escenarios en lenguaje de negocio (.feature).' },
    { nombre: 'NUnit', uso: 'Motor de ejecución de escenarios (OK / fallo).' },
    { nombre: 'Microsoft Playwright', uso: 'Control del navegador Chrome/Chromium contra SOT.' },
    { nombre: 'PowerShell', uso: 'Iniciar-Runner.bat (recomendado). No abrir Start-RunnerOperador.ps1 directo desde OneDrive.' },
    { nombre: 'Node.js / npm', uso: 'Build del frontend Angular (portable en RunnerOperador/.tools).' },
    { nombre: 'Jira REST API', uso: 'Lectura de tickets desde Generar. Email + API token Atlassian en Configuración.' },
    { nombre: 'OpenAI-compatible LLM', uso: 'Opcional en RunnerIA: pulir Gherkin y visión de capturas (key solo en el navegador).' },
    { nombre: 'OCR Windows (WinRT)', uso: 'Lee texto de pantallazos al Analizar cuando no hay API key LLM.' },
    { nombre: 'JSON + Configuration', uso: 'appsettings.json, overlays y secrets locales.' },
    { nombre: 'SQL Server (SQL SOT)', uso: 'Consultas auxiliares a la base SOT (UW_CASHIER, etc.). Se auditan en ConsultasDb.' },
    { nombre: 'Sybase ASE (COBIS)', uso: 'Consultas COBIS (cheques, fecha de proceso, etc.). SQL + resultado van al informe/ZIP.' },
    { nombre: 'ExtentReports', uso: 'Informe HTML paso a paso + ZIP de evidencias (incluye ConsultasDb).' }
  ];

  tecnologiasVisibles = computed(() => {
    if (this.workspace.esSotAccusys()) return this.tecnologias;
    return this.tecnologias.filter(
      (t) =>
        !/SQL SOT|COBIS|Release 9/i.test(t.nombre) &&
        !/SOT\.|UW_CASHIER|cheques/i.test(t.uso)
    );
  });

  async ngOnInit(): Promise<void> {
    if (!this.workspace.esSotAccusys()) {
      this.manualHtml.set(
        this.sanitizer.bypassSecurityTrustHtml(
          '<p class="muted">Manual técnico SOT no aplica a este proyecto. Usá las secciones de esta página.</p>'
        )
      );
      return;
    }
    try {
      const data = await firstValueFrom(this.api.getManual());
      const md = data.content || '';
      const { html, toc } = this.renderMarkdown(md);
      this.toc.set(toc);
      this.manualHtml.set(this.sanitizer.bypassSecurityTrustHtml(html));
      this.status.set('');
    } catch {
      this.status.set('No se pudo cargar MANUAL.md. Revisá que el servidor esté arriba.');
      this.manualHtml.set(
        this.sanitizer.bypassSecurityTrustHtml(
          '<p class="muted">Manual no disponible. Abrí <code>Iniciar-Runner.bat</code> e intentá de nuevo.</p>'
        )
      );
    }
  }

  toggleManual(): void {
    this.manualExpandido.update((v) => !v);
  }

  irA(id: string, ev?: Event): void {
    ev?.preventDefault();
    const el = document.getElementById(id);
    el?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  onManualClick(ev: MouseEvent): void {
    const a = (ev.target as HTMLElement | null)?.closest?.('a[data-manual-anchor]') as HTMLAnchorElement | null;
    if (!a) return;
    const id = a.getAttribute('data-manual-anchor');
    if (id) this.irA(id, ev);
  }

  private slug(texto: string): string {
    return String(texto || '')
      .normalize('NFD')
      .replace(/[\u0300-\u036f]/g, '')
      .toLowerCase()
      .replace(/`([^`]+)`/g, '$1')
      .replace(/<[^>]+>/g, '')
      .replace(/[.:,()[\]{}"'¿?¡!]/g, '')
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '');
  }

  private escapar(s: string): string {
    return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  }

  private escaparAttr(s: string): string {
    return this.escapar(String(s || '')).replace(/"/g, '&quot;');
  }

  private inline(texto: string): string {
    let t = this.escapar(texto);
    t = t.replace(/`([^`]+)`/g, '<code>$1</code>');
    t = t.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');
    t = t.replace(/\[([^\]]+)\]\(([^)]+)\)/g, (_, label, href) => {
      const safe = this.escaparAttr(href);
      if (String(href).startsWith('#')) {
        return `<a href="${safe}" data-manual-anchor="${safe.slice(1)}">${label}</a>`;
      }
      return `<a href="${safe}" target="_blank" rel="noopener">${label}</a>`;
    });
    return t;
  }

  private renderMarkdown(md: string): { html: string; toc: TocItem[] } {
    const lineas = md.replace(/\r\n/g, '\n').split('\n');
    let html = '';
    const toc: TocItem[] = [];
    let i = 0;
    let enLista = false;
    let enOrden = false;
    const usedIds = new Set<string>();

    const cerrarLista = () => {
      if (enLista) {
        html += '</ul>';
        enLista = false;
      }
      if (enOrden) {
        html += '</ol>';
        enOrden = false;
      }
    };

    const uniqueId = (base: string) => {
      let id = base || 'seccion';
      let n = 2;
      while (usedIds.has(id)) {
        id = `${base}-${n++}`;
      }
      usedIds.add(id);
      return id;
    };

    while (i < lineas.length) {
      const linea = lineas[i];

      if (linea.trim().startsWith('```')) {
        cerrarLista();
        i++;
        let code = '';
        while (i < lineas.length && !lineas[i].trim().startsWith('```')) {
          code += this.escapar(lineas[i]) + '\n';
          i++;
        }
        i++;
        html += `<pre><code>${code}</code></pre>`;
        continue;
      }

      if (
        linea.includes('|') &&
        i + 1 < lineas.length &&
        /^\s*\|?[\s:|-]+\|?\s*$/.test(lineas[i + 1]) &&
        lineas[i + 1].includes('-')
      ) {
        cerrarLista();
        const encabezados = linea
          .split('|')
          .map((c) => c.trim())
          .filter((c) => c.length);
        html +=
          '<div class="table-wrap"><table><thead><tr>' +
          encabezados.map((h) => `<th>${this.inline(h)}</th>`).join('') +
          '</tr></thead><tbody>';
        i += 2;
        while (i < lineas.length && lineas[i].includes('|')) {
          const celdas = lineas[i]
            .split('|')
            .map((c) => c.trim())
            .filter((c) => c.length);
          html += '<tr>' + celdas.map((c) => `<td>${this.inline(c)}</td>`).join('') + '</tr>';
          i++;
        }
        html += '</tbody></table></div>';
        continue;
      }

      const h = linea.match(/^(#{1,3})\s+(.+)$/);
      if (h) {
        cerrarLista();
        const level = h[1].length;
        const raw = h[2].trim();
        const id = uniqueId(this.slug(raw));
        const tag = level === 1 ? 'h1' : level === 2 ? 'h2' : 'h3';
        html += `<${tag} id="${id}">${this.inline(raw)}</${tag}>`;
        if (level <= 2) {
          toc.push({ id, label: raw.replace(/`/g, ''), level });
        }
        i++;
        continue;
      }

      if (/^---+\s*$/.test(linea.trim())) {
        cerrarLista();
        html += '<hr />';
        i++;
        continue;
      }

      if (/^>\s?/.test(linea)) {
        cerrarLista();
        let quote = '';
        while (i < lineas.length && /^>\s?/.test(lineas[i])) {
          quote += lineas[i].replace(/^>\s?/, '') + ' ';
          i++;
        }
        html += `<blockquote>${this.inline(quote.trim())}</blockquote>`;
        continue;
      }

      const ol = linea.match(/^\s*\d+\.\s+(.+)$/);
      if (ol) {
        if (enLista) {
          html += '</ul>';
          enLista = false;
        }
        if (!enOrden) {
          html += '<ol>';
          enOrden = true;
        }
        html += `<li>${this.inline(ol[1])}</li>`;
        i++;
        continue;
      }

      const ul = linea.match(/^\s*[-*]\s+(.+)$/);
      if (ul) {
        if (enOrden) {
          html += '</ol>';
          enOrden = false;
        }
        if (!enLista) {
          html += '<ul>';
          enLista = true;
        }
        html += `<li>${this.inline(ul[1])}</li>`;
        i++;
        continue;
      }

      if (!linea.trim()) {
        cerrarLista();
        i++;
        continue;
      }

      cerrarLista();
      html += `<p>${this.inline(linea.trim())}</p>`;
      i++;
    }
    cerrarLista();
    return { html, toc };
  }
}
