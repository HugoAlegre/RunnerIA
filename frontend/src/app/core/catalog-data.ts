export const CATEGORIAS: any[] = [
      {
        id: "regresion",
        nombre: "Regresión operativa",
        sub: "Alta de caja + Intercaja en Pesos, USD y EUR (sin Caja-Bóveda)",
        descripcion: "Corrida larga que combina alta de caja y pases intercaja en las tres monedas. No incluye Pases Caja-Bóveda.",
        escenarios: ["00"]
      },
      {
        id: "alta-caja",
        nombre: "Alta de caja",
        sub: "Crear caja, asignar, equivalencia COE y apertura",
        descripcion: "Flujo de alta de caja en Parametría: creación, asignación al usuario, validación de equivalencia COE y apertura en pesos, dólares y euro.",
        escenarios: ["01", "02", "02-USD", "02-EUR", "alta-caja-regresion"]
      },
      {
        id: "intercaja",
        nombre: "Pases intercaja",
        sub: "Enviar, aceptar y anular pases entre cajas",
        descripcion: "Pases de efectivo entre cajas Normal: limpieza de pendientes, pase aceptado en destino y anulado desde origen. Pesos, USD y EUR.",
        escenarios: ["05", "03", "04", "03-USD", "04-USD", "03-EUR", "04-EUR", "intercaja-regresion"]
      },
      {
        id: "caja-boveda",
        nombre: "Pases Caja-Bóveda (opcional)",
        sub: "Solo con caja bóveda asignada — fuera de la regresión estándar",
        descripcion: "Popup COE negativo en Pases Caja-Bóveda (Pesos y USD). Solo con bóveda asignada.",
        escenarios: ["06", "06-USD", "caja-boveda-regresion"]
      },
      {
        id: "cheques",
        nombre: "Cheques",
        sub: "Depósito e interdepósito COBIS: felices, judiciales, negativos y USD",
        descripcion: "Depósito e interdepósito COBIS: conexión, felices, judiciales, negativos y USD.",
        escenarios: ["08", "09", "10", "11", "12", "13", "14", "15", "16", "17", "18-USD", "19-USD", "cheques-regresion"]
      },
      {
        id: "retiro-efectivo",
        nombre: "Retiro de efectivo",
        sub: "Clientes → Retiro: cuentas COBIS rotativas por titularidad y moneda",
        descripcion: "Pruebas de retiro en Clientes > Retiro de efectivo. El robot busca cuentas COBIS que cumplan condiciones (titularidad, bloqueos, saldo) y deja el carrito en Pendiente o ejecutado según el caso.",
        escenarios: [
          "retiro-r01", "retiro-r02", "retiro-r03", "retiro-r04",
          "retiro-r05", "retiro-r06", "retiro-r07", "retiro-r08",
          "retiro-r09", "retiro-r10", "retiro-r11", "retiro-r12",
          "retiro-r13", "retiro-r14", "retiro-r15", "retiro-r16",
          "retiro-r17", "retiro-r18", "retiro-r19", "retiro-r20",
          "retiro-r21", "retiro-r22", "retiro-r23", "retiro-r24",
          "retiro-r25", "retiro-r26", "retiro-r27", "retiro-r28",
          "retiro-cf01", "retiro-cf02", "retiro-cf03", "retiro-cf04",
          "retiro-cf05", "retiro-cf06", "retiro-cf07", "retiro-conf-todo",
          "retiro-todo"
        ]
      },
      {
        id: "retiro-efectivo-cuentas",
        nombre: "Retiro por número de cuenta",
        sub: "Cuentas fijas parametrizadas · sin condiciones · empresa CUIT",
        descripcion: "Retiro RC01–RC03 por número de cuenta fijo (overlay retiro-cuentas).",
        escenarios: [
          "retiro-rc01", "retiro-rc02", "retiro-rc03",
          "retiro-cuentas-todo"
        ]
      },
      {
        id: "parametria-sc161",
        nombre: "Parametría — Relación transacción y perfil (SC-161)",
        sub: "Pantalla de parametría contable: acceso, alta, edición y validaciones",
        descripcion:
          "Ticket SC-161: pantalla Relación Transacción–Perfil Contable en Parametría. Suite automática P1–P4; casos manuales P5 (TA, PDF); negativos P6.",
        escenarios: [
          "18",
          "18-CA01", "18-CA02",
          "18-CA03", "18-CA04",
          "18-CA05", "18-CA06", "18-CA07", "18-CA09", "18-CA10",
          "18-CA08", "18-CA11", "18-CA12", "18-CA13", "18-CA14", "18-CA15",
          "18-CN01", "18-CN02", "18-CN03", "18-CN04", "18-CN05", "18-CN06",
          "parametria-sc161-regresion"
        ]
      },
      {
        id: "cierre-cuadre",
        nombre: "Cuadre y Cierre de Caja",
        sub: "Cuadre diario, billetaje, supervisión y diferencias COBIS",
        descripcion: "Pruebas del cierre operativo de caja en la sucursal configurada: pantalla Cuadre y cierre, billetaje, supervisión (sot1/sot3), popups COBIS y eliminación de cierre. Verificación host en cob_remesas..re_cierre cuando aplica.",
        escenarios: ["31", "31-CA01", "31-CA02", "31-CA03", "31-CA04", "31-CA05", "31-CN01", "cierre-cuadre-regresion"]
      },
      {
        id: "mejoras-sc416",
        nombre: "Cierre forzado — Nota de billetaje (SC-416)",
        sub: "Procesos de sucursal → Forzar cierre según tipo de caja",
        descripcion: "Ticket SC-416: al forzar el cierre de una caja desde Cierre de Sucursal, ¿debe mostrarse la Nota sobre billetaje? Compensada no la muestra; Normal y MiniBóveda sí. Solo abre el diálogo y cancela — no confirma el cierre real.",
        escenarios: ["32", "32-CA01", "32-CA02", "32-CA03", "mejoras-sc416-regresion"]
      },
      {
        id: "release-9",
        nombre: "Release 9",
        sub: "Tickets SC automatizados en Runner; T.O./TimeOut = QA manual fuera",
        descripcion: "Entrega Release 9: cada ítem es un ticket Jira SC con prueba automatizada. «Correr todo Release 9» ejecuta el lote completo. Tickets solo Excel/QA manual no aparecen aquí.",
        escenarios: [
          "r9-SC-161",
          "r9-SC-402",
          "r9-SC-414",
          "r9-SC-461",
          "r9-SC-471",
          "r9-SC-472",
          "r9-SC-476",
          "r9-SC-476-qa",
          "r9-SC-484",
          "r9-SC-485",
          "r9-SC-492",
          "r9-SC-493",
          "r9-todo"
        ]
      },
      {
        id: "cat-regresion-pruebas",
        nombre: "Correr todo",
        sub: "Regresión completa — total de pruebas generadas (módulo aparte)",
        descripcion: "Todas las pruebas @Prueba excepto Release 9 y stubs (@PruebaAutoStub).",
        escenarios: ["regresion-pruebas-todo"]
      },
      {
        id: "cat-diagnostico",
        nombre: "Verificar instalación",
        sub: "Comprobar que el PC puede ejecutar pruebas",
        descripcion: "Build + list-tests en segundos. No abre SOT. Usar antes de lotes grandes o la primera vez en tu PC.",
        escenarios: ["smoke-pipeline"]
      },
      {
        id: "grp-sin-clasificar",
        nombre: "Testing (sin clasificar)",
        sub: "Escenarios del asistente aún sin grupo de testing",
        descripcion: "Pruebas del asistente sin módulo asignado.",
        escenarios: [],
        testing: true
      }
    ];

export const ESCENARIOS: Record<string, any> = {
      "00": {
        num: "00", tipo: "smoke",
        titulo: "Regresión operativa completa",
        corto: "Alta de caja + Intercaja en Pesos, USD y EUR.",
        descripcion: "Qué hace: ejecuta en una sola corrida la regresión operativa estándar — escenarios de alta de caja (01–02 en las tres monedas) e intercaja (limpieza, pase aceptado y anulado en Pesos, USD y EUR). No incluye Pases Caja-Bóveda. Duración: varios minutos.",
        tag: "@AltaSinEquivalencia + @AltaConEquivalencia (Pesos/USD/EUR) + @LimpiezaPasesIntercaja + @OtrosIngresos (Pesos/USD/EUR) + @AnulacionPaseIntercaja (Pesos/USD/EUR)",
        resultado: "Regresión de las tres monedas ejecutada",
        script: "run-TodosLosFeatures.ps1", extraArgs: "",
        prerequisitos: [
          "Usuario con permisos de parametria, asignación, apertura e intercaja",
          "Sucursal configurada (131 San Martin por defecto)",
          "Al menos dos cajas Normal abiertas para Intercaja en cada moneda: Pesos, USD y EUR",
          "Tiempo estimado: varios minutos (suite completa)"
        ],
        evidencias: [
          "Informes HTML de cada bloque de moneda",
          "Alta 01: mensaje COE; Alta 02: cajas abiertas en Pesos, USD y EUR",
          "Intercaja aceptado y anulado en Pesos, USD y EUR (con limpieza previa)"
        ]
      },
      "regresion-pruebas-todo": {
        num: "P-ALL", tipo: "smoke",
        titulo: "Correr todo — regresión completa del proyecto",
        corto: "Todas las pruebas @Prueba excepto Release 9 y stubs.",
        descripcion: "Ejecuta todas las pruebas automatizadas del proyecto marcadas con @Prueba, excepto Release 9 (módulo propio) y @PruebaAutoStub (p. ej. SC-470 CA-01..04, steps Ignore). Es la regresión más amplia del Runner — puede tardar mucho.",
        tag: "Correr todo — @Prueba sin Release9 ni PruebaAutoStub",
        resultado: "Cumplir resultado esperado de cada prueba + informe HTML/ZIP",
        script: "run-PruebasTodos.ps1", extraArgs: "",
        prerequisitos: [
          "Configuración SOT válida (usuario/sucursal)",
          "Cajas/condiciones que requieran los escenarios incluidos",
          "Tiempo estimado: depende de cuántos escenarios haya"
        ],
        evidencias: [
          "Informe HTML Extent embebido (paso a paso de todas las pruebas)",
          "Capturas PNG por paso",
          "Descargar evidencias (ZIP)"
        ]
      },
      "01": {
        num: "01", tipo: "feliz",
        titulo: "Alta sin COE",
        corto: "Validar aviso de equivalencia pendiente.",
        descripcion: "Crea una caja, la asigna al usuario y valida en Apertura el mensaje de falta de equivalencia COE.",
        tag: "@AltaSinEquivalencia",
        resultado: "Mensaje COE visible",
        script: "run-AltaSinEquivalencia.ps1", extraArgs: "",
        prerequisitos: [
          "Usuario con permisos de parametria y asignacion de cajas",
          "Correlativo de caja disponible segun configuracion",
          "Sucursal configurada en appsettings"
        ],
        evidencias: [
          "Alta de caja registrada correctamente",
          "Asignacion visible en grilla de cajas",
          "Mensaje: Relacion caja SOT / Unidad COE no se dio de alta en SOT"
        ]
      },
      "02": {
        num: "02", tipo: "feliz",
        titulo: "Alta con apertura",
        corto: "Alta COE y apertura en pesos.",
        descripcion: "Mismo flujo del escenario 01, mas alta de equivalencia COE y apertura operativa en pesos.",
        tag: "@AltaConEquivalencia",
        resultado: "Caja abierta en pesos",
        script: "run-AltaCajaConEquivalencia.ps1", extraArgs: "",
        prerequisitos: [
          "Usuario con acceso a Parametria y Alta equivalencia COE",
          "Correlativo de caja disponible",
          "Fecha de bandeja alineada con fecha de proceso"
        ],
        evidencias: [
          "Equivalencia COE cargada sin errores",
          "Mensaje COE ya no aparece en Apertura",
          "Caja queda abierta solo en moneda pesos"
        ]
      },
      "02-USD": {
        num: "02-USD", tipo: "feliz",
        titulo: "Alta con apertura (USD)",
        corto: "Alta COE y apertura en dólares.",
        descripcion: "Mismo flujo del escenario 02. Solo cambia el toggle de la fila Dólares. Si pide supervisión, usa sot1.",
        tag: "@AltaConEquivalenciaUsd",
        resultado: "Caja abierta en Dolares",
        script: "run-AltaCajaConEquivalencia.ps1", extraArgs: "-Moneda Usd",
        prerequisitos: [
          "Mismos permisos que el escenario 02",
          "Supervisor sot1 configurado (Caja:UsuarioSupervisorApertura)",
          "Overlay appsettings.alta-usd.json"
        ],
        evidencias: [
          "Equivalencia COE cargada",
          "Toggle Dolares activo / estado Abierta",
          "Supervisión sot1 completada si apareció el diálogo"
        ]
      },
      "02-EUR": {
        num: "02-EUR", tipo: "feliz",
        titulo: "Alta con apertura (EUR)",
        corto: "Alta COE y apertura en euro.",
        descripcion: "Mismo flujo del escenario 02. Solo cambia el toggle de la fila Euro. Si pide supervisión, usa sot1.",
        tag: "@AltaConEquivalenciaEur",
        resultado: "Caja abierta en Euro",
        script: "run-AltaCajaConEquivalencia.ps1", extraArgs: "-Moneda Eur",
        prerequisitos: [
          "Mismos permisos que el escenario 02",
          "Supervisor sot1 configurado",
          "Overlay appsettings.alta-eur.json"
        ],
        evidencias: [
          "Equivalencia COE cargada",
          "Toggle Euro activo / estado Abierta",
          "Supervisión sot1 completada si apareció el diálogo"
        ]
      },
      "03": {
        num: "03", tipo: "feliz",
        titulo: "Interpase aceptado",
        corto: "Enviar y aceptar pase desde caja destino.",
        descripcion: "Circuito completo de pase intercaja: origen, destino, billetaje, confirmacion, recepcion y aceptacion.",
        tag: "@OtrosIngresos",
        resultado: "Aceptado",
        script: "run-Intercaja.ps1", extraArgs: "-Solo Aceptado",
        prerequisitos: [
          "Al menos dos cajas normales asignadas al usuario",
          "Caja origen con COE y saldo suficiente",
          "Fecha de bandeja = fecha de proceso",
          "Sin pases basura A confirmar (correr escenario 05 si hace falta)"
        ],
        evidencias: [
          "Pase visible en Enviados con estado A confirmar",
          "Detalle con monto y numero SOT correctos",
          "Pase final en Recibidos con estado Aceptado"
        ]
      },
      "04": {
        num: "04", tipo: "feliz",
        titulo: "Interpase anulado",
        corto: "Anular pase pendiente desde caja origen.",
        descripcion: "Envia un pase intercaja y lo anula desde la caja origen antes de aceptarlo en destino. Valida Anulado en Enviados (origen) y en Recibidos (destino).",
        tag: "@AnulacionPaseIntercaja",
        resultado: "Anulado en ambos lados",
        script: "run-AnulacionPaseIntercaja.ps1", extraArgs: "",
        prerequisitos: [
          "Caja origen operativa con saldo",
          "Caja destino distinta disponible",
          "No dejar pases basura previos (o correr limpieza)"
        ],
        evidencias: [
          "Pase aparece en Enviados: A confirmar",
          "Tras anular, estado Anulado en Enviados de origen",
          "Estado Anulado también en Recibidos de destino"
        ]
      },
      "05": {
        num: "05", tipo: "smoke",
        titulo: "Limpieza interpases",
        corto: "Resolver pases A confirmar.",
        descripcion: "Resuelve pases intercaja pendientes A confirmar anulando en Enviados o aceptando en Recibidos.",
        tag: "@LimpiezaPasesIntercaja",
        resultado: "Sin pendientes",
        script: "run-LimpiezaPasesIntercaja.ps1", extraArgs: "",
        prerequisitos: [
          "Usuario con cajas operativas asignadas",
          "Acceso a Pases Intercaja desde Acciones de Caja"
        ],
        evidencias: [
          "Pases A confirmar en Enviados anulados",
          "Pases A confirmar en Recibidos aceptados",
          "Si no hay pendientes, finaliza sin error"
        ]
      },
      "06": {
        num: "06-07", tipo: "negativo",
        titulo: "Pases Caja-Bóveda error COE",
        corto: "Opcional: requiere bóveda asignada.",
        descripcion: "NO forma parte de la regresión estándar. Ingresa a Pases Caja-Bóveda y selecciona cajas hasta que aparezca un popup de error COE (sin equivalencia u Operapendientes). Solo ejecutar si hay caja bóveda asignada al usuario.",
        tag: "@PasesCajaBovedaNegativo",
        resultado: "Popup COE mostrado",
        script: "run-PasesCajaBovedaNegativo.ps1", extraArgs: "",
        prerequisitos: [
          "OBLIGATORIO: caja tipo Bóveda asignada al usuario en la sucursal",
          "Usuario con acceso a Pases Caja - Bóveda desde Acciones de Caja",
          "Fuera de regresión 01–05: no correr si no hay bóveda (confunde evidencia)"
        ],
        evidencias: [
          "Aparece uno de los dos carteles de error COE",
          "Relacion caja SOT / Unidad COE, o Consulta COE / Operapendientes",
          "El dialogo se cierra con Entendido"
        ]
      },
      "03-USD": {
        num: "03-USD", tipo: "feliz",
        titulo: "Interpase aceptado (USD)",
        corto: "Enviar y aceptar pase en dólares.",
        descripcion: "Circuito intercaja en moneda Dolares. Detecta dinámicamente desde Apertura una caja Normal de origen con USD abierto y otra con las mismas características como destino.",
        tag: "@OtrosIngresosUsd",
        resultado: "Aceptado",
        script: "run-Intercaja.ps1", extraArgs: "-Solo UsdAceptado",
        prerequisitos: [
          "Al menos dos cajas Normal del usuario con USD abierto y COE",
          "Una caja se usa como origen y otra como destino",
          "Overlay appsettings.intercaja-usd.json"
        ],
        evidencias: [
          "Pase en Enviados: A confirmar",
          "Pase final en Recibidos: Aceptado",
          "Billetaje en denominaciones USD"
        ]
      },
      "04-USD": {
        num: "04-USD", tipo: "feliz",
        titulo: "Interpase anulado (USD)",
        corto: "Anular pase USD desde origen.",
        descripcion: "Envía un pase intercaja en dólares y lo anula desde la caja origen. Misma lógica que el escenario 04 en pesos.",
        tag: "@AnulacionPaseIntercajaUsd",
        resultado: "Anulado en ambos lados",
        script: "run-Intercaja.ps1", extraArgs: "-Solo UsdAnulado",
        prerequisitos: [
          "Caja origen USD operativa con saldo",
          "Caja destino distinta con USD",
          "Overlay appsettings.intercaja-usd.json"
        ],
        evidencias: [
          "Estado Anulado en Enviados de origen",
          "Estado Anulado en Recibidos de destino"
        ]
      },
      "03-EUR": {
        num: "03-EUR", tipo: "feliz",
        titulo: "Interpase aceptado (EUR)",
        corto: "Enviar y aceptar pase en euros.",
        descripcion: "Circuito intercaja en moneda Euro. Detecta dinámicamente origen y destino Normal con euros abiertos. Supervisión sot1 igual que Pesos/USD.",
        tag: "@OtrosIngresosEur",
        resultado: "Aceptado",
        script: "run-Intercaja.ps1", extraArgs: "-Solo EurAceptado",
        prerequisitos: [
          "Al menos dos cajas Normal del usuario con Euro abierto y COE",
          "Overlay appsettings.intercaja-eur.json"
        ],
        evidencias: [
          "Pase en Enviados: A confirmar",
          "Pase final en Recibidos: Aceptado",
          "Billetaje en denominaciones EUR"
        ]
      },
      "04-EUR": {
        num: "04-EUR", tipo: "feliz",
        titulo: "Interpase anulado (EUR)",
        corto: "Anular pase EUR desde origen.",
        descripcion: "Envía un pase intercaja en euros y lo anula desde la caja origen.",
        tag: "@AnulacionPaseIntercajaEur",
        resultado: "Anulado en ambos lados",
        script: "run-Intercaja.ps1", extraArgs: "-Solo EurAnulado",
        prerequisitos: [
          "Caja origen EUR operativa con saldo",
          "Caja destino distinta con Euro",
          "Overlay appsettings.intercaja-eur.json"
        ],
        evidencias: [
          "Estado Anulado en Enviados de origen",
          "Estado Anulado en Recibidos de destino"
        ]
      },
      "06-USD": {
        num: "06-USD", tipo: "negativo",
        titulo: "Pases Caja-Bóveda error COE (USD)",
        corto: "Detecta una caja USD desde Apertura.",
        descripcion: "Mismo popup COE negativo que el escenario 06, usando una caja Normal con USD abierto detectada dinámicamente desde Apertura.",
        tag: "@PasesCajaBovedaNegativoUsd",
        resultado: "Popup COE mostrado",
        script: "run-PasesCajaBovedaNegativo.ps1", extraArgs: "-Solo Usd",
        prerequisitos: [
          "Caja bóveda / acceso a Pases Caja-Bóveda",
          "Overlay appsettings.pases-caja-boveda-usd.json"
        ],
        evidencias: [
          "Popup COE (sin equivalencia u Operapendientes)",
          "Cierre con Entendido"
        ]
      },
      "30": {
        num: "30", tipo: "smoke",
        titulo: "Parametría — listado de cajas",
        corto: "Abre Parametría > Cajas en la sucursal configurada, lista y filtra.",
        descripcion: "Qué prueba: que el listado de cajas funciona en la sucursal del usuario. Dónde: Parametría > Cajas. El robot verifica la oficina configurada, filtra por correlativo y lee tipo y estado. Si hay permiso de edición, abre el detalle y cancela sin guardar. No modifica límites globales.",
        tag: "@ParametriaCajasSmoke131",
        resultado: "Listado y filtro OK en sucursal configurada",
        script: "run-ParametriaCajasSmoke.ps1", extraArgs: "",
        prerequisitos: [
          "Usuario con permiso de Parametría > Cajas (listado)",
          "Sucursal configurada en appsettings.json (DialogoSucursal) con al menos una caja cargada",
          "Overlay appsettings.cierre-cuadre.json (opcional; no fija sucursal)",
          "Edición de detalle es opcional (permiso sot:cajas-editar)"
        ],
        evidencias: [
          "Oficina configurada seleccionada (no Todas las sucursales)",
          "Listado filtrado por correlativo con tipo y estado visibles",
          "Formulario de detalle solo si hay botón Editar"
        ]
      },
      "31": {
        num: "31", tipo: "smoke",
        titulo: "Cuadre y cierre completo (smoke)",
        corto: "Elige caja abierta → cuadre → cierra → elimina cierre.",
        descripcion: "Qué prueba: el flujo de punta a punta de cuadre y cierre en la sucursal configurada. Dónde: Parametría > Límites de caja (Opera sobre = sucursal del usuario) y Acciones de Caja > Cuadre y cierre. El robot elige una caja abierta al azar (una por corrida), resuelve pases pendientes si hay botones rojos, carga Otros ingresos si el saldo es 0 (una sola vez), informa billetaje y cierra. Al final elimina el cierre para dejar la caja disponible. Supervisión: sot1 (local) y sot3 (control de billetaje).",
        tag: "@CierreCuadreSmoke131",
        resultado: "Cierre confirmado; caja abierta al final",
        script: "run-CierreCuadreSmoke.ps1", extraArgs: "",
        prerequisitos: [
          "Usuario con permisos de Cuadre y cierre, Otros ingresos y Parametría > Límites de caja",
          "Sucursal configurada con al menos una caja abierta en Pesos o USD",
          "Overlay appsettings.cierre-cuadre.json",
          "sot1 (supervisión local) y sot3 (control de billetaje) en secrets",
          "Opcional: CierreCuadre:MaxIntentosSeleccionCajaAleatoria (default 8)"
        ],
        evidencias: [
          "Opera sobre = sucursal configurada en Límites de caja",
          "Log: pool y orden aleatorio de cajas probadas",
          "Pases pendientes resueltos desde Cuadre (si había botones rojos)",
          "Botón Imprimir en pestaña Completado tras cerrar",
          "Cierre confirmado y eliminación de cierre (caja abierta al final)"
        ]
      },
      "31-CA01": {
        num: "31.1", tipo: "feliz",
        entregaQaExcel: true,
        titulo: "Cierre sin diferencia con COBIS",
        corto: "Caja alineada con COBIS — cierra sin popup de motivos.",
        descripcion: "Qué prueba: cierre cuando el saldo SOT coincide con COBIS (sin diferencia). Dónde: Cuadre y cierre. Pasos: informar billetaje igual al saldo, autorizar control de billetaje con sot3, confirmar cierre. No aparece el popup de motivos COBIS. Al final elimina el cierre para reutilizar la caja.",
        tag: "@CierreCuadreCA01",
        resultado: "Cierre y eliminación OK sin popup COBIS",
        script: "run-CierreCuadreModulo.ps1", extraArgs: "-Solo CA-01",
        prerequisitos: [
          "Sucursal configurada con caja Normal en Pesos sin diferencia COBIS/SOT",
          "Overlay appsettings.cierre-cuadre.json",
          "sot3 para Control de Billetaje"
        ],
        evidencias: [
          "Sin popup de motivos COBIS",
          "Billetaje informado y supervisión sot3 visible",
          "Botón Imprimir en pestaña Completado tras cerrar",
          "Eliminar cierre al final (caja reutilizable)",
          "Query COBIS re_cierre embebida en Excel (solo evidencia)"
        ]
      },
      "31-CA02": {
        num: "31.2", tipo: "feliz",
        entregaQaExcel: true,
        titulo: "Cierre con diferencia COBIS — aceptar motivos",
        corto: "Hay diferencia COBIS → popup → sot1 → billetaje → sot3.",
        descripcion: "Qué prueba: cierre cuando hay diferencia entre saldo SOT y COBIS y el operador acepta continuar. Dónde: Cuadre y cierre. Pasos: popup de motivos → Confirmar → supervisión sot1 → billetaje informado → supervisión sot3 → cierre confirmado. Al final elimina el cierre.",
        tag: "@CierreCuadreCA02",
        resultado: "Cierre OK tras aceptar diferencia COBIS",
        script: "run-CierreCuadreModulo.ps1", extraArgs: "-Solo CA-02",
        prerequisitos: [
          "Caja con diferencia COBIS/SOT cerrable en la sucursal configurada",
          "Overlay appsettings.cierre-cuadre.json",
          "sot1 y sot3 en secrets"
        ],
        evidencias: [
          "Popup de motivos COBIS visible",
          "Supervisión sot1 y sot3 con Autorizar visible",
          "Botón Imprimir en pestaña Completado tras cerrar",
          "Cierre confirmado y eliminación al final",
          "Query COBIS re_cierre embebida en Excel (solo evidencia)"
        ]
      },
      "31-CA03": {
        num: "31.3", tipo: "feliz",
        entregaQaExcel: true,
        titulo: "Eliminar Cierre de Caja",
        corto: "Caja ya cerrada → Eliminar cierre → sot1 → abierta.",
        descripcion: "Precondición: caja Normal en Pesos con cierre confirmado (botón «Eliminar cierre» visible). El robot NO cierra otra caja: selecciona esa caja cerrada, pulsa Eliminar cierre, Acepta el resumen, autoriza sot1 y verifica caja abierta. COBIS re_cierre solo como query embebida en Excel.",
        tag: "@CierreCuadreCA03",
        resultado: "Cierre eliminado; caja abierta en Cuadre",
        script: "run-CierreCuadreModulo.ps1", extraArgs: "-Solo CA-03",
        prerequisitos: [
          "Al menos una caja Normal en Pesos ya cerrada con botón «Eliminar cierre»",
          "Overlay appsettings.cierre-cuadre.json",
          "sot1 en secrets"
        ],
        evidencias: [
          "Excel hoja Evidencias: PNG embebidos + bloque Consultas BD (re_cierre)",
          "Log: Ingresando usuario «sot1» (eliminar cierre)",
          "Estado Abierta y botón Cierre de caja al final"
        ]
      },
      "31-CA04": {
        num: "31.4", tipo: "feliz",
        entregaQaExcel: true,
        titulo: "Cierre con sobrante — sot1",
        corto: "Descuadre sobrante → Registrar falla → sot1 → eliminar cierre.",
        descripcion: "Caja descuadrada por sobrante: billetaje distinto al saldo, Registrar falla con supervisión sot1, confirmar cierre y eliminar cierre con flujo explícito (resumen + sot1).",
        tag: "@CierreCuadreCA04",
        resultado: "Cierre con sobrante OK; caja abierta tras eliminar cierre",
        script: "run-CierreCuadreModulo.ps1", extraArgs: "-Solo CA-04",
        prerequisitos: [
          "Caja Normal abierta en Pesos",
          "Overlay appsettings.cierre-cuadre.json",
          "sot1 en secrets (falla de caja y eliminar cierre)"
        ],
        evidencias: [
          "Estado Caja descuadrada por sobrante",
          "Supervisión sot1 en Registrar falla y en eliminar cierre",
          "Supervisión sot3 en Control Billetaje post-falla",
          "Query COBIS re_cierre embebida en Excel (solo evidencia)",
          "Caja abierta al final"
        ]
      },
      "31-CA05": {
        num: "31.5", tipo: "feliz",
        entregaQaExcel: true,
        titulo: "Cierre con faltante — sot1",
        corto: "Descuadre faltante → Registrar falla → sot1 → eliminar cierre.",
        descripcion: "Caja descuadrada por faltante: billetaje distinto al saldo, Registrar falla con supervisión sot1, confirmar cierre y eliminar cierre con flujo explícito (resumen + sot1).",
        tag: "@CierreCuadreCA05",
        resultado: "Cierre con faltante OK; caja abierta tras eliminar cierre",
        script: "run-CierreCuadreModulo.ps1", extraArgs: "-Solo CA-05",
        prerequisitos: [
          "Caja Normal abierta en Pesos",
          "Overlay appsettings.cierre-cuadre.json",
          "sot1 en secrets (falla de caja y eliminar cierre)"
        ],
        evidencias: [
          "Estado Caja descuadrada por faltante",
          "Supervisión sot1 en Registrar falla y en eliminar cierre",
          "Supervisión sot3 en Control Billetaje post-falla",
          "Query COBIS re_cierre embebida en Excel (solo evidencia)",
          "Caja abierta al final"
        ]
      },
      "31-CN01": {
        num: "31.6", tipo: "negativo",
        entregaQaExcel: true,
        titulo: "Cierre con diferencia COBIS — cancelar",
        corto: "Hay diferencia COBIS → popup → Cancelar → no cierra.",
        descripcion: "Qué prueba: que Cancelar en el popup de motivos COBIS aborta el cierre. Dónde: Cuadre y cierre. Cuando hay diferencia COBIS/SOT y aparece el popup, el robot pulsa Cancelar. El cierre no debe confirmarse ni debe aparecer «Eliminar cierre».",
        tag: "@CierreCuadreCN01",
        resultado: "Cierre no confirmado tras Cancelar",
        script: "run-CierreCuadreModulo.ps1", extraArgs: "-Solo CN-01",
        prerequisitos: [
          "Caja con diferencia COBIS/SOT cerrable en la sucursal configurada",
          "Overlay appsettings.cierre-cuadre.json"
        ],
        evidencias: [
          "Popup de motivos COBIS visible",
          "Sin botón Eliminar cierre tras Cancelar"
        ]
      },
      "32": {
        num: "32", tipo: "mejora",
        titulo: "SC-416 — los tres tipos de caja (suite)",
        corto: "Compensada sin Nota; Normal y MiniBóveda con Nota.",
        descripcion: "Qué prueba: ticket SC-416 — si el diálogo de Forzar cierre muestra o no la sección «Nota de billetaje» según el tipo de caja. Dónde: Procesos de Sucursal > Cierre de Sucursal > Forzar cierre. Compensada (tipo 10, sin manejo de billetaje) NO debe mostrar Nota; Normal y MiniBóveda SÍ. El robot solo abre el diálogo, verifica y cancela — no confirma el cierre forzado. Correlativos de caja en Runner → Configuración → SC-416.",
        tag: "@CierreForzadoNota (@SC416_CA_01|02|03)",
        resultado: "Compensada sin Nota; Normal y MiniBóveda con Nota",
        script: "run-SC416-CierreForzadoNota.ps1", extraArgs: "",
        prerequisitos: [
          "Sucursal 131 abierta en fecha de proceso",
          "Cajas de prueba abiertas en Pesos con botón Forzar cierre visible",
          "Correlativos en Config → SC-416 (defaults: Compensada 4161, Normal 4162; MiniBóveda configurable)",
          "No confirma el cierre: solo observa el diálogo y cancela"
        ],
        evidencias: [
          "Diálogo Compensada sin sección Nota de billetaje",
          "Diálogo Normal con Nota de billetaje",
          "Diálogo MiniBóveda con Nota de billetaje"
        ]
      },
      "32-CA01": {
        num: "32.1", tipo: "mejora",
        titulo: "SC-416 CA-01 — Compensada sin Nota",
        corto: "Forzar cierre en Compensada: no debe aparecer la Nota.",
        descripcion: "Qué prueba: en una caja Compensada, el diálogo de Forzar cierre no debe mostrar la sección Nota de billetaje (esa caja no maneja billetaje). Dónde: Procesos de Sucursal > Cierre de Sucursal. Localiza la Compensada configurada, abre Forzar cierre, verifica ausencia de Nota y cancela sin confirmar.",
        tag: "@SC416_CA_01",
        resultado: "Diálogo sin sección Nota de billetaje",
        script: "run-SC416-CierreForzadoNota.ps1", extraArgs: "-Solo CA-01",
        prerequisitos: [
          "Caja Compensada abierta en Pesos (correlativo en Config → SC-416, default 4161)",
          "Botón Forzar cierre visible en Cierre de Sucursal"
        ],
        evidencias: [
          "Diálogo Confirmar cierre forzado sin sección Nota de billetaje"
        ]
      },
      "32-CA02": {
        num: "32.2", tipo: "mejora",
        titulo: "SC-416 CA-02 — Normal con Nota",
        corto: "Forzar cierre en Normal: debe aparecer la Nota de billetaje.",
        descripcion: "Qué prueba: en una caja Normal (maneja efectivo), el diálogo de Forzar cierre SÍ debe mostrar la Nota de billetaje. Dónde: Procesos de Sucursal > Cierre de Sucursal. Localiza la Normal configurada, abre Forzar cierre, verifica que la Nota está visible y cancela sin confirmar.",
        tag: "@SC416_CA_02",
        resultado: "Diálogo con Nota de billetaje visible",
        script: "run-SC416-CierreForzadoNota.ps1", extraArgs: "-Solo CA-02",
        prerequisitos: [
          "Caja Normal abierta en Pesos (correlativo en Config → SC-416, default 4162)",
          "Botón Forzar cierre visible en Cierre de Sucursal"
        ],
        evidencias: [
          "Diálogo con Nota de billetaje (texto sobre cierre sin billetaje informado)"
        ]
      },
      "18": {
        num: "18", tipo: "smoke",
        titulo: "SC-161 — suite automática (P1 a P4)",
        corto: "Acceso, listado, autocomplete, alta y edición (sin manuales).",
        descripcion: "Qué prueba: ticket SC-161 — pantalla Relación Transacción–Perfil Contable en Parametría. Ejecuta en una corrida los casos automáticos (acceso, filtro, autocomplete TRN/causa, altas y edición). Excluye casos manuales marcados @SC161_Manual (TA, PDF, catálogo F5). Jira: SC-161.",
        tag: "@Release9 @SC161 (!SC161_Manual)",
        resultado: "Suite ejecutada",
        script: "run-SC161-RelacionTransaccionPerfil.ps1", extraArgs: "",
        prerequisitos: [
          "Usuario con permiso sot:page_relacion-transaccion-perfil (salvo CN-06)",
          "Datos RelacionTransaccionPerfil en appsettings",
          "Excel: casos-prueba/SC-161 Evidencia.xlsx"
        ],
        evidencias: [
          "Informe HTML Extent de la corrida",
          "Capturas paso a paso"
        ]
      },
      "18-CA01": {
        num: "CA-01", tipo: "feliz",
        titulo: "P1 Acceso · Abrir pantalla Relación TRN–Perfil",
        corto: "Menú Parametría > Relación Transacción–Perfil.",
        descripcion: "Qué prueba: SC-161 CA-01 — un usuario con permiso puede abrir la pantalla. Dónde: Parametría > Relación Transacción–Perfil. Debe verse la bandeja de detalles.",
        tag: "@SC161_CA01",
        resultado: "Bandeja visible",
        script: "run-SC161-RelacionTransaccionPerfil.ps1", extraArgs: "-Solo CA-01",
        prerequisitos: ["Usuario con permiso de parametría"],
        evidencias: ["Bandeja Detalles visible"]
      },
      "18-CA02": {
        num: "CA-02", tipo: "feliz",
        titulo: "P1 Listado · Filtrar relaciones por texto",
        corto: "Filtro smart search por TRN/perfil.",
        descripcion: "Qué prueba: SC-161 CA-02 — el filtro de búsqueda del listado funciona por texto (TRN o perfil). Dónde: bandeja de la pantalla Relación TRN–Perfil.",
        tag: "@SC161_CA02",
        resultado: "Filtro OK",
        script: "run-SC161-RelacionTransaccionPerfil.ps1", extraArgs: "-Solo CA-02",
        prerequisitos: ["Usuario con permiso de parametría"],
        evidencias: ["Listado filtrado"]
      },
      "18-CA03": {
        num: "CA-03", tipo: "feliz",
        titulo: "P2 Autocomplete · Descripción de TRN (válida / inválida)",
        corto: "TRN válida vs inválida (999999).",
        descripcion: "Qué prueba: SC-161 CA-03 — autocomplete de descripción de transacción: TRN válida completa el campo; TRN inválida (999999) se rechaza o limpia. Dónde: formulario de alta/edición.",
        tag: "@SC161_CA03",
        resultado: "Validación TRN",
        script: "run-SC161-RelacionTransaccionPerfil.ps1", extraArgs: "-Solo CA-03",
        prerequisitos: ["COBIS / conector operativo", "TRN consulta 32"],
        evidencias: ["Descripción TRN informada", "TRN inválida limpia"]
      },
      "18-CA04": {
        num: "CA-04", tipo: "feliz",
        titulo: "P2 Autocomplete · Descripción de causa (válida / inválida)",
        corto: "Causa 1 vs 999.",
        descripcion: "Qué prueba: SC-161 CA-04 — autocomplete de descripción de causa: causa válida (ej. 1) informa descripción; causa inválida (999) no bloquea indebidamente. Dónde: formulario de alta/edición.",
        tag: "@SC161_CA04",
        resultado: "Validación causa",
        script: "run-SC161-RelacionTransaccionPerfil.ps1", extraArgs: "-Solo CA-04",
        prerequisitos: ["COBIS / conector operativo"],
        evidencias: ["Causa informada", "Causa inválida no bloquea"]
      },
      "18-CA05": {
        num: "CA-05", tipo: "feliz",
        titulo: "P3 Alta · Guardar relación con causa vacía",
        corto: "TRN dedicada + causa 0; rota si ya existe.",
        descripcion: "Qué prueba: SC-161 CA-05 — alta de relación con causa vacía (0). Dónde: formulario de alta. Si la TRN ya existe, el robot rota a otra TRN libre del catálogo.",
        tag: "@SC161_CA05",
        resultado: "Alta en listado",
        script: "run-SC161-RelacionTransaccionPerfil.ps1", extraArgs: "-Solo CA-05",
        prerequisitos: ["Perfil efectivo válido en appsettings", "Catálogo TRN alta libre"],
        evidencias: ["TRN reservada en listado"]
      },
      "18-CA06": {
        num: "CA-06", tipo: "feliz",
        titulo: "P3 Alta · Guardar relación con causa informada",
        corto: "TRN dedicada + causa 6; rota si ya existe.",
        descripcion: "Qué prueba: SC-161 CA-06 — alta de relación con causa informada (ej. 6). Dónde: formulario de alta. Recomendado correr antes de CA-10 (edición). Rota TRN si hay duplicado.",
        tag: "@SC161_CA06",
        resultado: "Alta con causa",
        script: "run-SC161-RelacionTransaccionPerfil.ps1", extraArgs: "-Solo CA-06",
        prerequisitos: ["Perfil efectivo válido", "Catálogo TRN alta libre"],
        evidencias: ["TRN reservada en listado"]
      },
      "18-CA07": {
        num: "CA-07", tipo: "feliz",
        titulo: "P3 Perfil · Autocompletar descripción al salir del campo",
        corto: "Blur perfil OIRECACHE / configurado.",
        descripcion: "Qué prueba: SC-161 CA-07 — al salir del campo perfil contable, autocompleta la descripción si el perfil existe. Dónde: formulario de alta/edición.",
        tag: "@SC161_CA07",
        resultado: "Perfil informado",
        script: "run-SC161-RelacionTransaccionPerfil.ps1", extraArgs: "-Solo CA-07",
        prerequisitos: ["Perfil efectivo en appsettings"],
        evidencias: ["Descripción de perfil autocompletada"]
      },
      "18-CA08": {
        num: "CA-08", tipo: "feliz",
        titulo: "P5 Manual · Catálogo F5 de perfiles contables",
        corto: "Doble clic / F5 catálogo (manual en lote auto).",
        descripcion: "SC-161 · CA-08 — Catálogo F5. Tag @SC161_Manual: fuera de corrida Todos.",
        tag: "@SC161_CA08 @SC161_Manual",
        resultado: "Catálogo / pendiente UI",
        script: "run-SC161-RelacionTransaccionPerfil.ps1", extraArgs: "-Solo CA-08",
        prerequisitos: ["UI catálogo perfiles disponible"],
        evidencias: ["Dialogo Listado de Perfiles Contables"]
      },
      "18-CA09": {
        num: "CA-09", tipo: "feliz",
        titulo: "P3 UI · Checkbox No Contabiliza deshabilita rubro cheques",
        corto: "Checkbox deshabilita/habilita perfil cheques.",
        descripcion: "Qué prueba: SC-161 CA-09 — el checkbox «No Contabiliza» deshabilita y rehabilita el rubro de cheques propios. Dónde: formulario de relación. Caso automático de UI.",
        tag: "@SC161_CA09",
        resultado: "UI OK",
        script: "run-SC161-RelacionTransaccionPerfil.ps1", extraArgs: "-Solo CA-09",
        prerequisitos: ["Usuario con permiso de parametría"],
        evidencias: ["Perfil Chq. Propios deshabilitado/habilitado"]
      },
      "18-CA10": {
        num: "CA-10", tipo: "feliz",
        titulo: "P4 Edición · Modificar relación existente y guardar",
        corto: "Doble clic fila + cambio perfil Efectivo.",
        descripcion: "Qué prueba: SC-161 CA-10 — edición de una relación existente (doble clic en fila, cambio de perfil Efectivo, guardar). Dónde: bandeja y diálogo de detalle. Ideal después de CA-06.",
        tag: "@SC161_CA10",
        resultado: "Edición OK",
        script: "run-SC161-RelacionTransaccionPerfil.ps1", extraArgs: "-Solo CA-10",
        prerequisitos: ["Relación editable en bandeja (ideal post CA-06)"],
        evidencias: ["Dialogo detalle actualizado"]
      },
      "18-CA11": {
        num: "CA-11", tipo: "feliz",
        titulo: "P5 Manual · Eliminar relación confirmando Sí",
        corto: "Menú contextual eliminar (manual).",
        descripcion: "SC-161 · CA-11 — Eliminación confirmada. @SC161_Manual.",
        tag: "@SC161_CA11 @SC161_Manual",
        resultado: "Eliminación / pendiente UI",
        script: "run-SC161-RelacionTransaccionPerfil.ps1", extraArgs: "-Solo CA-11",
        prerequisitos: ["Relación de prueba en bandeja"],
        evidencias: ["TRN ausente tras eliminar"]
      },
      "18-CA12": {
        num: "CA-12", tipo: "feliz",
        titulo: "P5 Manual · Tira auditora — evento de alta",
        corto: "Tira auditora alta (manual).",
        descripcion: "SC-161 · CA-12 — Evento de alta en TA. @SC161_Manual.",
        tag: "@SC161_CA12 @SC161_Manual",
        resultado: "TA alta / pendiente",
        script: "run-SC161-RelacionTransaccionPerfil.ps1", extraArgs: "-Solo CA-12",
        prerequisitos: ["Tira auditora con movimiento en ambiente"],
        evidencias: ["Tooltip TA completo"]
      },
      "18-CA13": {
        num: "CA-13", tipo: "feliz",
        titulo: "P5 Manual · Tira auditora — evento de modificación",
        corto: "Tira auditora modificación (manual).",
        descripcion: "SC-161 · CA-13 — Evento de modificación en TA. @SC161_Manual.",
        tag: "@SC161_CA13 @SC161_Manual",
        resultado: "TA modificación / pendiente",
        script: "run-SC161-RelacionTransaccionPerfil.ps1", extraArgs: "-Solo CA-13",
        prerequisitos: ["Tira auditora con movimiento en ambiente"],
        evidencias: ["Tooltip TA completo"]
      },
      "18-CA14": {
        num: "CA-14", tipo: "feliz",
        titulo: "P5 Manual · E2E Branch (deprecated: usar P3 Alta)",
        corto: "E2E Branch / causa 0 (manual).",
        descripcion: "SC-161 · CA-14 — E2E. @SC161_Manual.",
        tag: "@SC161_CA14 @SC161_Manual",
        resultado: "E2E / pendiente",
        script: "run-SC161-RelacionTransaccionPerfil.ps1", extraArgs: "-Solo CA-14",
        prerequisitos: ["Catálogo TRN alta libre"],
        evidencias: ["Flujo completo"]
      },
      "18-CA15": {
        num: "CA-15", tipo: "feliz",
        titulo: "P5 Manual · Generar PDF del listado",
        corto: "PDF del listado (manual).",
        descripcion: "SC-161 · CA-15 — Generar PDF. @SC161_Manual.",
        tag: "@SC161_CA15 @SC161_Manual",
        resultado: "PDF / pendiente",
        script: "run-SC161-RelacionTransaccionPerfil.ps1", extraArgs: "-Solo CA-15",
        prerequisitos: ["Datos en listado"],
        evidencias: ["Captura popup PDF"]
      },
      "18-CN01": {
        num: "CN-01", tipo: "negativo",
        titulo: "P6 Negativo · Guardar sin TRN / TRN inválida",
        corto: "Rechazo al guardar sin TRN / inexistente.",
        descripcion: "SC-161 · CN-01 — Validación TRN. @SC161_Manual.",
        tag: "@SC161_CN01 @SC161_Manual",
        resultado: "Rechazo",
        script: "run-SC161-RelacionTransaccionPerfil.ps1", extraArgs: "-Solo CN-01",
        prerequisitos: ["Usuario con permiso de parametría"],
        evidencias: ["Mensaje DATOS REQUERIDOS / ERROR"]
      },
      "18-CN02": {
        num: "CN-02", tipo: "negativo",
        titulo: "P6 Negativo · Perfil obligatorio (sin No Contabiliza)",
        corto: "Sin perfil y sin No Contabiliza.",
        descripcion: "SC-161 · CN-02 — Perfil mandatorio. @SC161_Manual.",
        tag: "@SC161_CN02 @SC161_Manual",
        resultado: "Rechazo",
        script: "run-SC161-RelacionTransaccionPerfil.ps1", extraArgs: "-Solo CN-02",
        prerequisitos: ["Usuario con permiso de parametría"],
        evidencias: ["Mensaje mandatorio / Rubro"]
      },
      "18-CN03": {
        num: "CN-03", tipo: "negativo",
        titulo: "P6 Negativo · Perfil inexistente se limpia o rechaza",
        corto: "Perfil inválido se limpia o rechaza.",
        descripcion: "SC-161 · CN-03 — Perfil inexistente. @SC161_Manual.",
        tag: "@SC161_CN03 @SC161_Manual",
        resultado: "Limpia/rechazo",
        script: "run-SC161-RelacionTransaccionPerfil.ps1", extraArgs: "-Solo CN-03",
        prerequisitos: ["Usuario con permiso de parametría"],
        evidencias: ["Campo limpio o rechazo al guardar"]
      },
      "18-CN04": {
        num: "CN-04", tipo: "negativo",
        titulo: "P6 Negativo · Alta duplicada misma TRN+Causa",
        corto: "Misma TRN+Causa → ERROR; Aceptar carteles.",
        descripcion: "SC-161 · CN-04 — Duplicado. Espera ERROR AL INSERTAR / ya existe y cadena de carteles Aceptar. @SC161_Manual.",
        tag: "@SC161_CN04 @SC161_Manual",
        resultado: "Error duplicado",
        script: "run-SC161-RelacionTransaccionPerfil.ps1", extraArgs: "-Solo CN-04",
        prerequisitos: ["Relación 32+causa 1 existente (o se crea en Given)"],
        evidencias: ["Cartel ERROR + mensaje no satisfactorio"]
      },
      "18-CN05": {
        num: "CN-05", tipo: "negativo",
        titulo: "P6 Negativo · Cancelar eliminación (relación permanece)",
        corto: "Cancelar diálogo de eliminar (manual).",
        descripcion: "SC-161 · CN-05 — Cancelar eliminación. @SC161_Manual.",
        tag: "@SC161_CN05 @SC161_Manual",
        resultado: "Relación permanece",
        script: "run-SC161-RelacionTransaccionPerfil.ps1", extraArgs: "-Solo CN-05",
        prerequisitos: ["Relación visible en bandeja"],
        evidencias: ["TRN sigue en listado"]
      },
      "18-CN06": {
        num: "CN-06", tipo: "negativo",
        titulo: "P6 Negativo · Usuario sin permiso de parametría",
        corto: "Usuario cajero sot3 sin acceso (manual).",
        descripcion: "SC-161 · CN-06 — Acceso denegado. @SC161_Manual.",
        tag: "@SC161_CN06 @SC161_Manual",
        resultado: "Acceso denegado",
        script: "run-SC161-RelacionTransaccionPerfil.ps1", extraArgs: "-Solo CN-06",
        prerequisitos: ["UsuarioSinPermiso (sot3) en appsettings"],
        evidencias: ["not_access / sin menú"]
      },
      "r9-SC-161": {
        num: "R9-161", tipo: "feliz",
        titulo: "SC-161 — Parametría Relación Transacción y Perfil Contable",
        corto: "Suite automática P1–P4 (sin @SC161_Manual: TA, PDF, CN).",
        descripcion: "Qué prueba: ticket SC-161 — pantalla Relación Transacción–Perfil Contable. Ejecuta solo casos automáticos (excluye @SC161_Manual). Negativos CN y TA/PDF son manuales en el módulo Parametría SC-161.",
        tag: "@Release9 @SC161 (!SC161_Manual)",
        resultado: "Casos SC-161 auto OK",
        script: "run-SC161-RelacionTransaccionPerfil.ps1", extraArgs: "",
        prerequisitos: [
          "Usuario con permiso de parametría",
          "Sucursal configurada"
        ],
        evidencias: [
          "Informe HTML suite SC-161",
          "Capturas paso a paso"
        ]
      },
      "r9-SC-402": {
        num: "R9-402", tipo: "feliz",
        titulo: "SC-402 — Correcciones en dar alta Equivalencia Unidad COE",
        corto: "CA-01…CA-04: digitos, max 4, vacios, alta valida.",
        descripcion: "Qué prueba: ticket SC-402 — reglas del campo Unidad en alta de equivalencia COE (CA-01 a CA-04). Dónde: Parametría > Equivalencias.",
        tag: "@Release9 @PruebaRun_SC_402",
        resultado: "Reglas Unidad COE OK",
        script: "run-PruebaFeature.ps1", extraArgs: "-Feature Release-9/SC-402_Equivalencia_Unidad_COE.feature",
        prerequisitos: ["Caja reciente para equivalencia", "Permiso parametría"],
        evidencias: ["Informe HTML + PNG por paso"]
      },
      "r9-SC-414": {
        num: "R9-414", tipo: "feliz",
        titulo: "SC-414 — En cierre caja al ir a Ver Minitesoro no se posiciona en la caja correspondiente",
        corto: "Desde cierre, Ver Minitesoro abre en la caja del cierre.",
        descripcion: "Qué prueba: ticket SC-414 — al pulsar Ver Minitesoro desde el cierre de una caja, debe abrirse posicionado en esa misma caja. Dónde: Cuadre y cierre / Minitesoro.",
        tag: "@Release9 @PruebaRun_SC_414",
        resultado: "Minitesoro en caja correcta",
        script: "run-PruebaFeature.ps1", extraArgs: "-Feature Release-9/SC-414_Ver_minitesoro_posiciona_caja.feature",
        prerequisitos: ["Caja abierta en cierre", "Usuario con permisos"],
        evidencias: ["Captura Minitesoro con caja correcta"]
      },
      "r9-SC-461": {
        num: "R9-461", tipo: "feliz",
        titulo: "SC-461 — Mensaje en supervisiones que requieren rol de nivel mayor a 1",
        corto: "Mensaje cuando el rol del supervisor no alcanza el nivel requerido.",
        descripcion: "Qué prueba: ticket SC-461 — al intentar autorizar con un usuario de nivel bajo, debe mostrarse mensaje de nivel insuficiente. Dónde: pantalla que requiere supervisión.",
        tag: "@Release9 @PruebaRun_SC_461",
        resultado: "Mensaje de nivel insuficiente",
        script: "run-PruebaFeature.ps1", extraArgs: "-Feature Release-9/SC-461_Supervision_nivel_insuficiente.feature",
        prerequisitos: ["Usuario con nivel bajo para la supervisión"],
        evidencias: ["Mensaje visible en pantalla"]
      },
      "r9-SC-471": {
        num: "R9-471", tipo: "feliz",
        titulo: "SC-471 — En pase intercaja no muestra saldo actualizado al pasar de bandeja",
        corto: "El saldo se actualiza al pasar entre bandejas del pase.",
        descripcion: "Qué prueba: ticket SC-471 — al cambiar de bandeja durante un pase intercaja, el saldo mostrado debe actualizarse correctamente. Dónde: Pases intercaja.",
        tag: "@Release9 @PruebaRun_SC_471",
        resultado: "Saldo coherente entre bandejas",
        script: "run-PruebaFeature.ps1", extraArgs: "-Feature Release-9/SC-471_Pase_intercaja_saldo_bandeja.feature",
        prerequisitos: ["Dos cajas operativas", "Pase intercaja en curso"],
        evidencias: ["Saldo en cada bandeja"]
      },
      "r9-SC-472": {
        num: "R9-472", tipo: "feliz",
        titulo: "SC-472 — Reporte Historico cierres de caja",
        corto: "Reporte histórico de cierres visible y coherente.",
        descripcion: "Qué prueba: ticket SC-472 — reporte Histórico de cierres de caja muestra los cierres previos de la sucursal. Dónde: reportes / histórico de cierres.",
        tag: "@Release9 @PruebaRun_SC_472",
        resultado: "Historial visible y coherente",
        script: "run-PruebaFeature.ps1", extraArgs: "-Feature Release-9/SC-472_Historial_cierres_caja.feature",
        prerequisitos: ["Cierres previos en sucursal"],
        evidencias: ["Grilla/reporte histórico"]
      },
      "r9-SC-476": {
        num: "R9-476", tipo: "feliz",
        titulo: "SC-476 — Nuevo mensaje de confirmacion en Desasignación de Caja",
        corto: "Gherkin CA-01/CA-02: mensaje con caja y usuario; cancelar no desasigna.",
        descripcion: "Qué prueba: ticket SC-476 — diálogo de desasignación con caja y usuario (CA-01/CA-02 automáticos). Los 12 casos Xray completos están en «Entrega QA 12 casos».",
        tag: "@Release9 @PruebaRun_SC_476",
        resultado: "Mensaje con caja y usuario; cancelar mantiene asignación",
        script: "run-PruebaFeature.ps1", extraArgs: "-Feature Release-9/SC-476_Mensaje_desasignacion_caja.feature",
        prerequisitos: ["Caja asignada en parametría cajas"],
        evidencias: ["Diálogo de confirmación", "Grilla tras cancelar"]
      },
      "r9-SC-476-qa": {
        num: "R9-476-QA", tipo: "feliz",
        entregaQaExcel: false,
        titulo: "SC-476 — Entrega QA (12 casos Xray)",
        corto: "Regenera Excel con 12 casos y evidencias PNG desde casos.json.",
        descripcion: "Empaqueta la entrega QA de SC-476: 12 casos con títulos limpios (sin TC/CX), hojas Casos + Evidencias + Queries. Fuente: casos-prueba/Release-9/SC-476/casos.json. Opcional recaptura: run-SC476-QaEntrega.ps1 -RecapturarEvidencias.",
        tag: "@SC476_QaEntrega",
        resultado: "SC-476-Casos-y-Evidencias.xlsx listo para Xray / CX-4471",
        script: "run-SC476-QaEntrega.ps1", extraArgs: "",
        prerequisitos: [
          "casos-prueba/Release-9/SC-476/casos.json",
          "Evidencias PNG en evidencias/SC476-CX-*"
        ],
        evidencias: ["Excel 12 casos", "ZIP QA casos-de-prueba"]
      },
      "r9-SC-484": {
        num: "R9-484", tipo: "feliz",
        titulo: "SC-484 — Modificar label Saldo Actual por Saldo de caja en Pases Intercaja y con COE",
        corto: "En Pases Intercaja y COE dice «Saldo de caja» (no «Saldo actual»).",
        descripcion: "Qué prueba: ticket SC-484 — el texto del saldo en pantallas de pases debe decir «Saldo de caja» en lugar de «Saldo actual». Dónde: Pases intercaja y equivalencias COE.",
        tag: "@Release9 @PruebaRun_SC_484",
        resultado: "Label Saldo de caja visible",
        script: "run-PruebaFeature.ps1", extraArgs: "-Feature Release-9/SC-484_Saldo_de_caja_pases.feature",
        prerequisitos: ["Caja operativa con COE"],
        evidencias: ["Label en pantalla de pases"]
      },
      "r9-SC-485": {
        num: "R9-485", tipo: "feliz",
        titulo: "SC-485 — Modificar label Saldo Actual por Saldo de caja en Transacciones Monetarias y Resumen Cierre de Caja",
        corto: "En Transacciones Monetarias y resumen de cierre dice «Saldo de caja».",
        descripcion: "Qué prueba: ticket SC-485 — el label «Saldo de caja» debe aparecer en Transacciones Monetarias y en el resumen de cierre/cuadre. Dónde: TM y Cuadre y cierre.",
        tag: "@Release9 @PruebaRun_SC_485",
        resultado: "Label Saldo de caja en TM y cierre",
        script: "run-PruebaFeature.ps1", extraArgs: "-Feature Release-9/SC-485_Saldo_de_caja_TM_resumen.feature",
        prerequisitos: ["Caja operativa"],
        evidencias: ["Label en TM y cuadre/cierre"]
      },
      "r9-SC-492": {
        num: "R9-492", tipo: "feliz",
        titulo: "SC-492 — Después de generar nuevo pase siempre posicionarse en bandeja Enviados",
        corto: "Después de generar un pase, queda en la bandeja Enviados.",
        descripcion: "Qué prueba: ticket SC-492 — al generar un pase intercaja nuevo, la pantalla debe posicionarse en la bandeja Enviados. Dónde: Pases intercaja.",
        tag: "@Release9 @PruebaRun_SC_492",
        resultado: "Bandeja Enviados seleccionada",
        script: "run-PruebaFeature.ps1", extraArgs: "-Feature Release-9/SC-492_Posicion_bandeja_Enviados.feature",
        prerequisitos: ["Cajas para pase intercaja"],
        evidencias: ["Bandeja Enviados activa"]
      },
      "r9-SC-493": {
        num: "R9-493", tipo: "feliz",
        titulo: "SC-493 — Mensaje en cierre de caja cuando no se realiza el cierre en COE",
        corto: "Aviso cuando se cierra caja sin haber cerrado en COE.",
        descripcion: "Qué prueba: ticket SC-493 — al intentar cerrar una caja que no tiene cierre en COE, debe mostrarse un mensaje de advertencia. Dónde: Cuadre y cierre.",
        tag: "@Release9 @PruebaRun_SC_493",
        resultado: "Mensaje de cierre sin COE",
        script: "run-PruebaFeature.ps1", extraArgs: "-Feature Release-9/SC-493_Mensaje_cierre_sin_COE.feature",
        prerequisitos: ["Caja abierta", "Sin cierre COE previo"],
        evidencias: ["Mensaje en pantalla de cierre"]
      },
      "r9-todo": {
        num: "R9-ALL", tipo: "smoke",
        regresionModulo: true,
        titulo: "Regresión — Release 9 (todos los tickets automáticos)",
        corto: "Corre todos los tickets SC automatizados de Release 9.",
        descripcion: "Ejecuta en una sola corrida todos los tickets de Release 9 que están en el Runner (SC-161, SC-402, SC-414, SC-461, SC-471, SC-472, SC-476, SC-484, SC-485, SC-492, SC-493). No incluye tickets solo QA manual (T.O./TimeOut).",
        tag: "@Release9 + SC-161",
        resultado: "Todos los tickets R9 automatizables OK",
        script: "run-Release9-Todos.ps1", extraArgs: "",
        prerequisitos: [
          "Configuración SOT válida",
          "Condiciones de cada ticket según prerequisitos individuales"
        ],
        evidencias: [
          "Informe HTML combinado"
        ]
      },
      "32-CA03": {
        num: "32.3", tipo: "mejora",
        titulo: "SC-416 CA-03 — MiniBóveda con Nota",
        corto: "Forzar cierre en MiniBóveda: debe aparecer la Nota de billetaje.",
        descripcion: "Qué prueba: en una caja MiniBóveda (maneja efectivo), el diálogo de Forzar cierre SÍ debe mostrar la Nota de billetaje. Dónde: Procesos de Sucursal > Cierre de Sucursal. Localiza la MiniBóveda (correlativo en Config o búsqueda por texto en pantalla), abre Forzar cierre, verifica la Nota y cancela sin confirmar.",
        tag: "@SC416_CA_03",
        resultado: "Diálogo con Nota de billetaje visible",
        script: "run-SC416-CierreForzadoNota.ps1", extraArgs: "-Solo CA-03",
        prerequisitos: [
          "Caja MiniBóveda abierta en Pesos (correlativo en Config → SC-416; vacío = busca por texto Mini/bóveda)",
          "Botón Forzar cierre visible en Cierre de Sucursal"
        ],
        evidencias: [
          "Diálogo MiniBóveda con Nota de billetaje visible"
        ]
      },
      "08": {
        num: "08", tipo: "smoke",
        titulo: "Conexión COBIS",
        corto: "Smoke de conexión y fecha de proceso.",
        descripcion: "Verifica la conexión con COBIS (SYBSRV2) y la fecha de proceso. No genera transacciones monetarias.",
        tag: "@DepositoChequesConexionCobis",
        resultado: "Conexión COBIS OK",
        script: "run-DepositoInterdepositoCheques.ps1", extraArgs: "-Solo ConexionCobis",
        prerequisitos: [
          "VPN / acceso a COBIS SYBSRV2 192.168.50.121:7410",
          "Usuario portiz de solo lectura disponible"
        ],
        evidencias: [
          "Conexión COBIS establecida",
          "Fecha de proceso leída correctamente"
        ]
      },
      "09": {
        num: "09", tipo: "feliz",
        titulo: "Interdepósito ahorros otra sucursal",
        corto: "Carrito Pendiente sin ejecutar.",
        descripcion: "Carga un interdepósito de ahorros de otra sucursal y deja el carrito en estado Pendiente, sin ejecutar la transacción.",
        tag: "@InterdepositoAhorrosOtraSucursal",
        resultado: "Carrito Pendiente",
        script: "run-DepositoInterdepositoCheques.ps1", extraArgs: "-Solo Interdeposito",
        prerequisitos: [
          "Conexión COBIS operativa (ver escenario 08)",
          "Cuenta de ahorros de otra sucursal válida"
        ],
        evidencias: [
          "Carrito cargado en estado Pendiente",
          "No se ejecuta transacción monetaria"
        ]
      },
      "10": {
        num: "10", tipo: "feliz",
        titulo: "Depósito/Interdepósito completo",
        corto: "Flujo E2E ejecutado (con TM).",
        descripcion: "Flujo feliz completo: carga, ejecuta el carrito y valida la Transacción Monetaria en estado Ejecutada.",
        tag: "@DepositoInterdepositoCompleto",
        resultado: "TM Ejecutada",
        script: "run-DepositoInterdepositoCheques.ps1", extraArgs: "-Solo Completo",
        prerequisitos: [
          "Conexión COBIS operativa",
          "Cuenta válida con depósito de cheques posible"
        ],
        evidencias: [
          "Carrito Ejecutada",
          "Detalle en Transacciones Monetarias: Ejecutada",
          "Búsqueda por número de producto (cuenta COBIS)"
        ]
      },
      "11": {
        num: "11", tipo: "feliz",
        titulo: "Interdepósito judicial",
        corto: "Flujo judicial completo (con TM).",
        descripcion: "Flujo feliz E2E para cuenta judicial: completa el circuito y valida la TM en estado Ejecutada.",
        tag: "@DepositoInterdepositoJudicial",
        resultado: "TM Ejecutada",
        script: "run-DepositoChequesFiltro.ps1", extraArgs: "-Categoria DepositoInterdepositoJudicial",
        prerequisitos: [
          "Conexión COBIS operativa",
          "Cuenta judicial válida"
        ],
        evidencias: [
          "Flujo judicial completo sin errores",
          "Detalle en Transacciones Monetarias: Ejecutada"
        ]
      },
      "12": {
        num: "12", tipo: "feliz",
        titulo: "Interdepósito corriente",
        corto: "Flujo cuenta corriente (con TM).",
        descripcion: "Flujo feliz E2E para cuenta corriente: completa el circuito y valida la TM en estado Ejecutada.",
        tag: "@DepositoInterdepositoCorriente",
        resultado: "TM Ejecutada",
        script: "run-DepositoInterdepositoCheques.ps1", extraArgs: "-Solo Corriente",
        prerequisitos: [
          "Conexión COBIS operativa",
          "Cuenta corriente válida"
        ],
        evidencias: [
          "Flujo corriente completo sin errores",
          "Detalle en Transacciones Monetarias: Ejecutada"
        ]
      },
      "13": {
        num: "13", tipo: "negativo",
        titulo: "Cuenta bloqueada",
        corto: "Error al Buscar (cuenta bloqueada).",
        descripcion: "Caso negativo: al buscar una cuenta bloqueada, el sistema debe mostrar error y no permitir avanzar.",
        tag: "@DepositoInterdepositoBloqueado",
        resultado: "Error de cuenta bloqueada",
        script: "run-DepositoChequesFiltro.ps1", extraArgs: "-Categoria DepositoInterdepositoBloqueado",
        prerequisitos: [
          "Conexión COBIS operativa",
          "Cuenta bloqueada conocida para la prueba"
        ],
        evidencias: [
          "Error al Buscar la cuenta bloqueada",
          "No se genera transacción monetaria"
        ]
      },
      "14": {
        num: "14", tipo: "negativo",
        titulo: "Sin depósito de cheques",
        corto: "COP/CANT en cero.",
        descripcion: "Caso negativo: importe (COP) y cantidad (CANT) en cero, no se puede continuar el depósito.",
        tag: "@DepositoInterdepositoSinDepositoCheques",
        resultado: "No continúa (COP/CANT cero)",
        script: "run-DepositoChequesFiltro.ps1", extraArgs: "-Categoria DepositoInterdepositoSinDepositoCheques",
        prerequisitos: [
          "Conexión COBIS operativa",
          "Cuenta válida sin cargar cheques"
        ],
        evidencias: [
          "COP y CANT en cero",
          "El flujo no permite finalizar"
        ]
      },
      "15": {
        num: "15", tipo: "negativo",
        titulo: "Cancelar judicial",
        corto: "Carrito vacío, TM no aparece.",
        descripcion: "Caso negativo: se cancela un depósito judicial. El carrito queda vacío y en Transacciones Monetarias NO debe figurar el depósito.",
        tag: "@DepositoInterdepositoCancelarJudicial",
        resultado: "TM ausente (cancelado)",
        script: "run-DepositoChequesFiltro.ps1", extraArgs: "-Categoria DepositoInterdepositoCancelarJudicial",
        prerequisitos: [
          "Conexión COBIS operativa",
          "Cuenta judicial válida"
        ],
        evidencias: [
          "Carrito queda vacío tras cancelar",
          "En Transacciones Monetarias NO aparece el depósito"
        ]
      },
      "16": {
        num: "16", tipo: "negativo",
        titulo: "Boleta inválida",
        corto: "Boleta/DV inválido (no finaliza).",
        descripcion: "Caso negativo: boleta o dígito verificador inválido; el depósito no debe finalizar.",
        tag: "@DepositoInterdepositoBoletaInvalida",
        resultado: "No finaliza (boleta inválida)",
        script: "run-DepositoChequesFiltro.ps1", extraArgs: "-Categoria DepositoInterdepositoBoletaInvalida",
        prerequisitos: [
          "Conexión COBIS operativa",
          "Cuenta válida"
        ],
        evidencias: [
          "Boleta / DV inválido rechazado",
          "El flujo no finaliza"
        ]
      },
      "17": {
        num: "17", tipo: "negativo",
        titulo: "Boleta no numérica",
        corto: "BOL solo dígitos.",
        descripcion: "Caso negativo: el campo boleta (BOL) solo admite dígitos; se valida el rechazo de valores no numéricos.",
        tag: "@DepositoInterdepositoBoletaNoNumerica",
        resultado: "Rechaza no numérico",
        script: "run-DepositoChequesFiltro.ps1", extraArgs: "-Categoria DepositoInterdepositoBoletaNoNumerica",
        prerequisitos: [
          "Conexión COBIS operativa",
          "Cuenta válida"
        ],
        evidencias: [
          "El campo BOL rechaza caracteres no numéricos",
          "El flujo no finaliza con boleta inválida"
        ]
      },
      "18-USD": {
        num: "18", tipo: "feliz",
        titulo: "Depósito USD misma sucursal",
        corto: "Cuenta ahorro USD + TM Ejecutada.",
        descripcion: "Depósito de cheques en dólares (caja 6380). Overlay appsettings.deposito-interdeposito-cheques-usd.json.",
        tag: "@DepositoUsdMismaSucursal",
        resultado: "TM Ejecutada",
        script: "run-DepositoChequesFiltro.ps1", extraArgs: "-Categoria DepositoUsdMismaSucursal -Usd",
        prerequisitos: [
          "Caja 6380 con dólares abiertos",
          "Cuenta ahorro USD misma sucursal en COBIS",
          "VPN / COBIS operativo"
        ],
        evidencias: [
          "Carrito Ejecutada",
          "Detalle TM: Ejecutada"
        ]
      },
      "19-USD": {
        num: "19", tipo: "feliz",
        titulo: "Interdepósito USD otra sucursal",
        corto: "Cuenta ahorro USD otra sucursal + TM.",
        descripcion: "Interdepósito de cheques en dólares (caja 6380). Overlay USD.",
        tag: "@DepositoInterdepositoUsd",
        resultado: "TM Ejecutada",
        script: "run-DepositoChequesFiltro.ps1", extraArgs: "-Categoria DepositoInterdepositoUsd -Usd",
        prerequisitos: [
          "Caja 6380 con dólares abiertos",
          "Cuenta ahorro USD de otra sucursal en COBIS",
          "VPN / COBIS operativo"
        ],
        evidencias: [
          "Carrito Ejecutada",
          "Detalle TM: Ejecutada"
        ]
      },
      "retiro-r01": {
        num: "R01", tipo: "feliz",
        titulo: "CA pesos individual",
        corto: "Caja de ahorro, titularidad individual.",
        descripcion: "Retiro CA pesos con cuenta COBIS individual: sin bloqueo, saldo suficiente, cliente rotativo, monto aleatorio acorde.",
        tag: "@RetiroCaIndividual",
        resultado: "Carrito Pendiente",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroCaIndividual",
        prerequisitos: [
          "★ CRITERIO: CA individual, bloqueos=0, saldo >= mínimo",
          "Conexión COBIS y caja Pesos abierta"
        ],
        evidencias: ["Log [COBIS] titularidad Individual", "Carrito 1 Pendiente"]
      },
      "retiro-r02": {
        num: "R02", tipo: "feliz",
        titulo: "CA pesos conjunta",
        corto: "Caja de ahorro, titularidad conjunta.",
        descripcion: "Retiro CA pesos con cuenta COBIS conjunta.",
        tag: "@RetiroCaConjunta",
        resultado: "Carrito Pendiente",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroCaConjunta",
        prerequisitos: ["Conexión COBIS y caja Pesos abierta"],
        evidencias: ["Log [COBIS] titularidad Conjunta", "Carrito 1 Pendiente"]
      },
      "retiro-r03": {
        num: "R03", tipo: "feliz",
        titulo: "CA pesos indistinta",
        corto: "Caja de ahorro, titularidad indistinta.",
        descripcion: "Retiro CA pesos con cuenta COBIS indistinta.",
        tag: "@RetiroCaIndistinta",
        resultado: "Carrito Pendiente",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroCaIndistinta",
        prerequisitos: ["Conexión COBIS y caja Pesos abierta"],
        evidencias: ["Log [COBIS] titularidad Indistinta", "Carrito 1 Pendiente"]
      },
      "retiro-r04": {
        num: "R04", tipo: "feliz",
        titulo: "CA pesos categorizada",
        corto: "Caja de ahorro, titularidad categorizada.",
        descripcion: "Retiro CA pesos con cuenta COBIS categorizada (pool reducido; revisar SaldoMinimoCategorizada).",
        tag: "@RetiroCaCategorizada",
        resultado: "Carrito Pendiente",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroCaCategorizada",
        prerequisitos: [
          "Conexión COBIS y caja Pesos abierta",
          "Pool categorizada CA en COBIS"
        ],
        evidencias: ["Log [COBIS] titularidad Categorizada", "Carrito 1 Pendiente"]
      },
      "retiro-r05": {
        num: "R05", tipo: "feliz",
        titulo: "CC pesos individual",
        corto: "Cuenta corriente, titularidad individual.",
        descripcion: "Retiro CC pesos con cuenta COBIS individual.",
        tag: "@RetiroCcIndividual",
        resultado: "Carrito 1 transacción",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroCcIndividual",
        prerequisitos: ["Conexión COBIS y caja Pesos abierta"],
        evidencias: ["Log [COBIS] titularidad Individual", "Carrito 1 transacción"]
      },
      "retiro-r06": {
        num: "R06", tipo: "feliz",
        titulo: "CC pesos conjunta",
        corto: "Cuenta corriente, titularidad conjunta.",
        descripcion: "Retiro CC pesos con cuenta COBIS conjunta.",
        tag: "@RetiroCcConjunta",
        resultado: "Carrito 1 transacción",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroCcConjunta",
        prerequisitos: ["Conexión COBIS y caja Pesos abierta"],
        evidencias: ["Log [COBIS] titularidad Conjunta", "Carrito 1 transacción"]
      },
      "retiro-r07": {
        num: "R07", tipo: "feliz",
        titulo: "CC pesos indistinta",
        corto: "Cuenta corriente, titularidad indistinta.",
        descripcion: "Retiro CC pesos con cuenta COBIS indistinta.",
        tag: "@RetiroCcIndistinta",
        resultado: "Carrito 1 transacción",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroCcIndistinta",
        prerequisitos: ["Conexión COBIS y caja Pesos abierta"],
        evidencias: ["Log [COBIS] titularidad Indistinta", "Carrito 1 transacción"]
      },
      "retiro-r08": {
        num: "R08", tipo: "feliz",
        titulo: "CC pesos categorizada",
        corto: "Cuenta corriente, titularidad categorizada.",
        descripcion: "Retiro CC pesos con cuenta COBIS categorizada.",
        tag: "@RetiroCcCategorizada",
        resultado: "Carrito 1 transacción",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroCcCategorizada",
        prerequisitos: [
          "Conexión COBIS y caja Pesos abierta",
          "Pool categorizada CC en COBIS"
        ],
        evidencias: ["Log [COBIS] titularidad Categorizada", "Carrito 1 transacción"]
      },
      "retiro-r09": {
        num: "R09", tipo: "feliz",
        titulo: "CA judicial pesos individual",
        corto: "Judicial, titularidad individual.",
        descripcion: "Retiro CA judicial pesos con cuenta COBIS individual.",
        tag: "@RetiroJudicialIndividual",
        resultado: "Carrito 1 transacción",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroJudicialIndividual",
        prerequisitos: ["Conexión COBIS y caja Pesos abierta"],
        evidencias: ["Log [COBIS] judicial individual", "Carrito 1 transacción"]
      },
      "retiro-r10": {
        num: "R10", tipo: "feliz",
        titulo: "CA judicial pesos conjunta",
        corto: "Judicial, titularidad conjunta.",
        descripcion: "Retiro CA judicial pesos con cuenta COBIS conjunta.",
        tag: "@RetiroJudicialConjunta",
        resultado: "Carrito 1 transacción",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroJudicialConjunta",
        prerequisitos: ["Conexión COBIS y caja Pesos abierta"],
        evidencias: ["Log [COBIS] judicial conjunta", "Carrito 1 transacción"]
      },
      "retiro-r11": {
        num: "R11", tipo: "feliz",
        titulo: "CA judicial pesos indistinta",
        corto: "Judicial, titularidad indistinta.",
        descripcion: "Retiro CA judicial pesos con cuenta COBIS indistinta.",
        tag: "@RetiroJudicialIndistinta",
        resultado: "Carrito 1 transacción",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroJudicialIndistinta",
        prerequisitos: ["Conexión COBIS y caja Pesos abierta"],
        evidencias: ["Log [COBIS] judicial indistinta", "Carrito 1 transacción"]
      },
      "retiro-r12": {
        num: "R12", tipo: "feliz",
        titulo: "CA judicial pesos categorizada",
        corto: "Judicial, titularidad categorizada.",
        descripcion: "Retiro CA judicial pesos con cuenta COBIS categorizada.",
        tag: "@RetiroJudicialCategorizada",
        resultado: "Carrito 1 transacción",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroJudicialCategorizada",
        prerequisitos: [
          "Conexión COBIS y caja Pesos abierta",
          "Pool categorizada judicial en COBIS"
        ],
        evidencias: ["Log [COBIS] judicial categorizada", "Carrito 1 transacción"]
      },
      "retiro-r13": {
        num: "R13", tipo: "feliz",
        titulo: "CA USD individual",
        corto: "Ahorro USD, titularidad individual.",
        descripcion: "Retiro CA USD con cuenta COBIS individual y caja dólares.",
        tag: "@RetiroCaUsdIndividual",
        resultado: "Carrito 1 Pendiente",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroCaUsdIndividual",
        prerequisitos: ["Conexión COBIS y caja USD abierta (overlay appsettings.retiro-usd.json, caja 6380)"],
        evidencias: ["Log [COBIS] USD individual", "Carrito 1 Pendiente"]
      },
      "retiro-r14": {
        num: "R14", tipo: "feliz",
        titulo: "CA USD conjunta",
        corto: "Ahorro USD, titularidad conjunta.",
        descripcion: "Retiro CA USD con cuenta COBIS conjunta.",
        tag: "@RetiroCaUsdConjunta",
        resultado: "Carrito 1 Pendiente",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroCaUsdConjunta",
        prerequisitos: ["Conexión COBIS y caja USD abierta (overlay appsettings.retiro-usd.json)"],
        evidencias: ["Log [COBIS] USD conjunta", "Carrito 1 Pendiente"]
      },
      "retiro-r15": {
        num: "R15", tipo: "feliz",
        titulo: "CA USD indistinta",
        corto: "Ahorro USD, titularidad indistinta.",
        descripcion: "Retiro CA USD con cuenta COBIS indistinta.",
        tag: "@RetiroCaUsdIndistinta",
        resultado: "Carrito 1 Pendiente",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroCaUsdIndistinta",
        prerequisitos: ["Conexión COBIS y caja USD abierta (overlay appsettings.retiro-usd.json)"],
        evidencias: ["Log [COBIS] USD indistinta", "Carrito 1 Pendiente"]
      },
      "retiro-r16": {
        num: "R16", tipo: "feliz",
        titulo: "CA USD categorizada",
        corto: "Ahorro USD, titularidad categorizada.",
        descripcion: "Retiro CA USD con cuenta COBIS categorizada.",
        tag: "@RetiroCaUsdCategorizada",
        resultado: "Carrito 1 Pendiente",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroCaUsdCategorizada",
        prerequisitos: ["Conexión COBIS, caja USD y pool categorizada CA USD (overlay retiro-usd)"],
        evidencias: ["Log [COBIS] USD categorizada", "Carrito 1 Pendiente"]
      },
      "retiro-r17": {
        num: "R17", tipo: "feliz",
        titulo: "CC USD individual",
        corto: "Corriente USD, titularidad individual.",
        descripcion: "Retiro CC USD con cuenta COBIS individual.",
        tag: "@RetiroCcUsdIndividual",
        resultado: "Carrito 1 transacción",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroCcUsdIndividual",
        prerequisitos: ["Conexión COBIS y caja USD abierta (overlay appsettings.retiro-usd.json)"],
        evidencias: ["Log [COBIS] CC USD individual", "Carrito 1 transacción"]
      },
      "retiro-r18": {
        num: "R18", tipo: "feliz",
        titulo: "CC USD conjunta",
        corto: "Corriente USD, titularidad conjunta.",
        descripcion: "Retiro CC USD con cuenta COBIS conjunta.",
        tag: "@RetiroCcUsdConjunta",
        resultado: "Carrito 1 transacción",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroCcUsdConjunta",
        prerequisitos: ["Conexión COBIS y caja USD abierta (overlay appsettings.retiro-usd.json)"],
        evidencias: ["Log [COBIS] CC USD conjunta", "Carrito 1 transacción"]
      },
      "retiro-r19": {
        num: "R19", tipo: "feliz",
        titulo: "CC USD indistinta",
        corto: "Corriente USD, titularidad indistinta.",
        descripcion: "Retiro CC USD con cuenta COBIS indistinta.",
        tag: "@RetiroCcUsdIndistinta",
        resultado: "Carrito 1 transacción",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroCcUsdIndistinta",
        prerequisitos: ["Conexión COBIS y caja USD abierta (overlay appsettings.retiro-usd.json)"],
        evidencias: ["Log [COBIS] CC USD indistinta", "Carrito 1 transacción"]
      },
      "retiro-r20": {
        num: "R20", tipo: "feliz",
        titulo: "CC USD categorizada",
        corto: "Corriente USD, titularidad categorizada.",
        descripcion: "Retiro CC USD con cuenta COBIS categorizada.",
        tag: "@RetiroCcUsdCategorizada",
        resultado: "Carrito 1 transacción",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroCcUsdCategorizada",
        prerequisitos: ["Conexión COBIS, caja USD y pool categorizada CC USD (overlay retiro-usd)"],
        evidencias: ["Log [COBIS] CC USD categorizada", "Carrito 1 transacción"]
      },
      "retiro-r21": {
        num: "R21", tipo: "feliz",
        titulo: "CA EUR individual",
        corto: "Ahorro EUR, titularidad individual.",
        descripcion: "Retiro CA EUR con cuenta COBIS individual y caja euro.",
        tag: "@RetiroCaEurIndividual",
        resultado: "Carrito 1 Pendiente",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroCaEurIndividual",
        prerequisitos: ["Conexión COBIS y caja Euro abierta (overlay appsettings.retiro-eur.json)"],
        evidencias: ["Log [COBIS] EUR individual", "Carrito 1 Pendiente"]
      },
      "retiro-r22": {
        num: "R22", tipo: "feliz",
        titulo: "CA EUR conjunta",
        corto: "Ahorro EUR, titularidad conjunta.",
        descripcion: "Retiro CA EUR con cuenta COBIS conjunta.",
        tag: "@RetiroCaEurConjunta",
        resultado: "Carrito 1 Pendiente",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroCaEurConjunta",
        prerequisitos: ["Conexión COBIS y caja Euro abierta (overlay appsettings.retiro-eur.json)"],
        evidencias: ["Log [COBIS] EUR conjunta", "Carrito 1 Pendiente"]
      },
      "retiro-r23": {
        num: "R23", tipo: "feliz",
        titulo: "CA EUR indistinta",
        corto: "Ahorro EUR, titularidad indistinta.",
        descripcion: "Retiro CA EUR con cuenta COBIS indistinta.",
        tag: "@RetiroCaEurIndistinta",
        resultado: "Carrito 1 Pendiente",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroCaEurIndistinta",
        prerequisitos: ["Conexión COBIS y caja Euro abierta (overlay appsettings.retiro-eur.json)"],
        evidencias: ["Log [COBIS] EUR indistinta", "Carrito 1 Pendiente"]
      },
      "retiro-r24": {
        num: "R24", tipo: "feliz",
        titulo: "CA EUR categorizada",
        corto: "Ahorro EUR, titularidad categorizada.",
        descripcion: "Retiro CA EUR con cuenta COBIS categorizada.",
        tag: "@RetiroCaEurCategorizada",
        resultado: "Carrito 1 Pendiente",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroCaEurCategorizada",
        prerequisitos: ["Conexión COBIS, caja Euro y pool categorizada CA EUR (overlay retiro-eur)"],
        evidencias: ["Log [COBIS] EUR categorizada", "Carrito 1 Pendiente"]
      },
      "retiro-r25": {
        num: "R25", tipo: "feliz",
        titulo: "CC EUR individual",
        corto: "Corriente EUR, titularidad individual.",
        descripcion: "Retiro CC EUR con cuenta COBIS individual.",
        tag: "@RetiroCcEurIndividual",
        resultado: "Carrito 1 transacción",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroCcEurIndividual",
        prerequisitos: ["Conexión COBIS y caja Euro abierta (overlay appsettings.retiro-eur.json)"],
        evidencias: ["Log [COBIS] CC EUR individual", "Carrito 1 transacción"]
      },
      "retiro-r26": {
        num: "R26", tipo: "feliz",
        titulo: "CC EUR conjunta",
        corto: "Corriente EUR, titularidad conjunta.",
        descripcion: "Retiro CC EUR con cuenta COBIS conjunta.",
        tag: "@RetiroCcEurConjunta",
        resultado: "Carrito 1 transacción",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroCcEurConjunta",
        prerequisitos: ["Conexión COBIS y caja Euro abierta (overlay appsettings.retiro-eur.json)"],
        evidencias: ["Log [COBIS] CC EUR conjunta", "Carrito 1 transacción"]
      },
      "retiro-r27": {
        num: "R27", tipo: "feliz",
        titulo: "CC EUR indistinta",
        corto: "Corriente EUR, titularidad indistinta.",
        descripcion: "Retiro CC EUR con cuenta COBIS indistinta.",
        tag: "@RetiroCcEurIndistinta",
        resultado: "Carrito 1 transacción",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroCcEurIndistinta",
        prerequisitos: ["Conexión COBIS y caja Euro abierta (overlay appsettings.retiro-eur.json)"],
        evidencias: ["Log [COBIS] CC EUR indistinta", "Carrito 1 transacción"]
      },
      "retiro-r28": {
        num: "R28", tipo: "feliz",
        titulo: "CC EUR categorizada",
        corto: "Corriente EUR, titularidad categorizada.",
        descripcion: "Retiro CC EUR con cuenta COBIS categorizada.",
        tag: "@RetiroCcEurCategorizada",
        resultado: "Carrito 1 transacción",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroCcEurCategorizada",
        prerequisitos: ["Conexión COBIS, caja Euro y pool categorizada CC EUR (overlay retiro-eur)"],
        evidencias: ["Log [COBIS] CC EUR categorizada", "Carrito 1 transacción"]
      },
      "retiro-todo": {
        num: "R-ALL", tipo: "smoke",
        regresionModulo: true,
        titulo: "Regresión — Retiro de efectivo (todo el módulo)",
        corto: "Suite R01–R28 (titularidades y monedas) + conformidad CF01–CF07.",
        descripcion: "Ejecuta todos los escenarios del módulo Retiro de efectivo: cuentas COBIS rotativas por titularidad (pesos, judicial, USD, EUR) y casos de conformidad de firmantes. Requiere COBIS y cajas abiertas según moneda.",
        tag: "@RetiroEfectivo",
        resultado: "Suite retiro OK",
        script: "run-RetiroEfectivo.ps1", extraArgs: "",
        prerequisitos: [
          "Conexión COBIS",
          "Cajas Pesos, USD y Euro abiertas según escenarios a ejecutar",
          "Config Retiro efectivo revisada en Runner"
        ],
        evidencias: [
          "Informe HTML suite retiro"
        ]
      },
      "retiro-cf01": {
        num: "CF01", tipo: "feliz",
        titulo: "Conformidad CA indistinta (1 firmante)",
        corto: "INDISTINTA → un slide.",
        descripcion: "Retiro CA pesos indistinta; conformidad elige un solo firmante.",
        tag: "@RetiroConfIndistinta",
        resultado: "Carrito 1 transacción",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroConfIndistinta",
        prerequisitos: ["COBIS AH indistinta con saldo"],
        evidencias: ["Log Regla titularidad INDISTINTA", "Carrito"]
      },
      "retiro-cf02": {
        num: "CF02", tipo: "feliz",
        titulo: "Conformidad CA conjunta (2 firmantes)",
        corto: "CONJUNTA → dos slides.",
        descripcion: "Retiro CA pesos conjunta; conformidad selecciona dos firmantes presentes.",
        tag: "@RetiroConfConjunta",
        resultado: "Carrito 1 transacción",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroConfConjunta",
        prerequisitos: ["COBIS AH conjunta con saldo"],
        evidencias: ["Log Regla titularidad CONJUNTA", "Carrito"]
      },
      "retiro-cf03": {
        num: "CF03", tipo: "feliz",
        titulo: "Conformidad CA categorizada (A o B+B)",
        corto: "CATEGORIZADA → 1×A o 2×B.",
        descripcion: "Retiro CA pesos categorizada; conformidad por categoría en tabla.",
        tag: "@RetiroConfCategorizada",
        resultado: "Carrito 1 transacción",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroConfCategorizada",
        prerequisitos: ["COBIS AH categorizada con saldo"],
        evidencias: ["Log CATEGORIZADA", "Carrito"]
      },
      "retiro-cf04": {
        num: "CF04", tipo: "feliz",
        titulo: "Conformidad CA unipersonal (1 firmante)",
        corto: "UNIPERSONAL → un slide.",
        descripcion: "Retiro CA pesos individual; un solo firmante. Sin condiciones: sot1 tras elegir firmante (RC01).",
        tag: "@RetiroConfUnipersonal",
        resultado: "Carrito 1 transacción",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroConfUnipersonal",
        prerequisitos: ["COBIS AH individual con saldo"],
        evidencias: ["Log Regla UNIPERSONAL", "Carrito"]
      },
      "retiro-cf05": {
        num: "CF05", tipo: "feliz",
        titulo: "Conformidad CC conjunta (2 firmantes)",
        corto: "CC CONJUNTA → dos slides.",
        descripcion: "Retiro CC pesos conjunta; dos firmantes presentes.",
        tag: "@RetiroConfCcConjunta",
        resultado: "Carrito 1 transacción",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroConfCcConjunta",
        prerequisitos: ["COBIS CC conjunta con saldo"],
        evidencias: ["Log CONJUNTA", "Carrito"]
      },
      "retiro-cf06": {
        num: "CF06", tipo: "feliz",
        titulo: "Conformidad CC categorizada (A o B+B)",
        corto: "CC CATEGORIZADA → 1×A o 2×B.",
        descripcion: "Retiro CC pesos categorizada; selección por categoría.",
        tag: "@RetiroConfCcCategorizada",
        resultado: "Carrito 1 transacción",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroConfCcCategorizada",
        prerequisitos: ["COBIS CC categorizada con saldo"],
        evidencias: ["Log CATEGORIZADA", "Carrito"]
      },
      "retiro-cf07": {
        num: "CF07", tipo: "feliz",
        titulo: "Conformidad Validación con Firma (⋮)",
        corto: "Firmas computadas → more_vert → Validación con Firma.",
        descripcion: "Tipo de caso firmas computadas: sin huellero, menú ⋮ del diálogo → Validación con Firma (prioridad sobre slides/FND). XPath: //app-signature-biometry-dialog//mat-icon[normalize-space()='more_vert'].",
        tag: "@RetiroConfValidacionFirma",
        resultado: "Carrito 1 transacción; log validacion_con_firma o fallback slides",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=RetiroConfValidacionFirma",
        prerequisitos: [
          "COBIS CA individual con saldo",
          "Preferible cuenta con firma en cobis..firmas / SOT habilita Validación con Firma"
        ],
        evidencias: [
          "Log Circuito elegido: validacion_con_firma",
          "Carrito 1 transacción"
        ]
      },
      "retiro-conf-todo": {
        num: "CF-ALL", tipo: "smoke",
        titulo: "Conformidad de firmantes (CF01–CF07)",
        corto: "Todos los casos de conformidad y Validación con Firma.",
        descripcion: "Ejecuta los escenarios CF01–CF07: selección de firmantes según titularidad (individual, conjunta, categorizada, unipersonal) y Validación con Firma cuando aplica. Dónde: Clientes > Retiro de efectivo > diálogo de conformidad.",
        tag: "@Conformidad",
        resultado: "Suite conformidad OK",
        script: "run-RetiroEfectivo.ps1", extraArgs: "-Filtro Category=Conformidad",
        prerequisitos: ["COBIS", "Caja pesos abierta"],
        evidencias: ["Informe HTML conformidad"]
      },
      "retiro-rc01": {
        num: "RC01", tipo: "feliz",
        titulo: "Sin condiciones vigentes",
        corto: "Cuenta fija SinCondiciones01.",
        descripcion: "Retiro CA pesos con NumeroCuenta en RetiroEfectivoCuentas:Cuentas:SinCondiciones01.",
        tag: "@RetiroCuentaSinCondiciones01",
        resultado: "Carrito 1 transacción",
        script: "run-RetiroEfectivoCuentas.ps1", extraArgs: "-Filtro Category=RetiroCuentaSinCondiciones01",
        prerequisitos: [
          "Completar NumeroCuenta en appsettings.retiro-cuentas.json",
          "Cuenta validada Operación V: CUENTA SIN CONDICIONES VIGENTES"
        ],
        evidencias: ["Log claveCfg SinCondiciones01", "Carrito 1 transacción"]
      },
      "retiro-rc02": {
        num: "RC02", tipo: "feliz",
        titulo: "CA empresa CUIT",
        corto: "Cuenta fija EmpresaCa01.",
        descripcion: "Retiro CA pesos empresa con NumeroCuenta parametrizado.",
        tag: "@RetiroCuentaEmpresaCa01",
        resultado: "Carrito 1 transacción",
        script: "run-RetiroEfectivoCuentas.ps1", extraArgs: "-Filtro Category=RetiroCuentaEmpresaCa01",
        prerequisitos: ["NumeroCuenta EmpresaCa01 en overlay retiro-cuentas"],
        evidencias: ["Log tipoDocSOT 11", "Carrito 1 transacción"]
      },
      "retiro-rc03": {
        num: "RC03", tipo: "feliz",
        titulo: "CC empresa CUIT",
        corto: "Cuenta fija EmpresaCc01.",
        descripcion: "Retiro CC pesos empresa con NumeroCuenta parametrizado.",
        tag: "@RetiroCuentaEmpresaCc01",
        resultado: "Carrito 1 transacción",
        script: "run-RetiroEfectivoCuentas.ps1", extraArgs: "-Filtro Category=RetiroCuentaEmpresaCc01",
        prerequisitos: ["NumeroCuenta EmpresaCc01 en overlay retiro-cuentas"],
        evidencias: ["Log subtipo C", "Carrito 1 transacción"]
      },
      "retiro-cuentas-todo": {
        num: "RC-ALL", tipo: "smoke",
        regresionModulo: true,
        titulo: "Regresión — Retiro por número de cuenta",
        corto: "RC01–RC03 con cuentas fijas del overlay retiro-cuentas.",
        descripcion: "Ejecuta los tres escenarios de retiro por número de cuenta fijo (sin condiciones, CA empresa CUIT, CC empresa CUIT). Requiere completar los números de cuenta en appsettings.retiro-cuentas.json.",
        tag: "@ModuloRetiroEfectivoCuentas",
        resultado: "Suite RC OK",
        script: "run-RetiroEfectivoCuentas.ps1", extraArgs: "",
        prerequisitos: ["appsettings.retiro-cuentas.json con NumeroCuenta", "Caja pesos ofi 131"],
        evidencias: ["Informe HTML suite RC"]
      },
      "alta-caja-regresion": {
        num: "REG", tipo: "smoke",
        regresionModulo: true,
        titulo: "Regresión — Alta de caja (todo el módulo)",
        corto: "Corre 01, 02 Pesos/USD/EUR en una sola corrida.",
        descripcion: "Ejecuta todos los escenarios del módulo Alta de caja: alta sin COE, alta con apertura en pesos, dólares y euro. Genera informe HTML y ZIP de evidencias.",
        resultado: "Todas las pruebas del módulo cumplen el resultado esperado",
        evidencias: [
          "Informe HTML Extent",
          "ZIP QA con capturas embebidas"
        ]
      },
      "intercaja-regresion": {
        num: "REG", tipo: "smoke",
        regresionModulo: true,
        titulo: "Regresión — Intercaja (todo el módulo)",
        corto: "Limpieza + pase aceptado y anulado en Pesos, USD y EUR.",
        descripcion: "Ejecuta todos los escenarios del módulo Intercaja: limpieza de pases pendientes, pase aceptado y anulado en las tres monedas.",
        resultado: "Todas las pruebas del módulo cumplen el resultado esperado",
        evidencias: ["Informe HTML", "ZIP QA"]
      },
      "caja-boveda-regresion": {
        num: "REG", tipo: "smoke",
        regresionModulo: true,
        titulo: "Regresión — Pases Caja-Bóveda",
        corto: "Popup COE negativo en Pesos y USD (requiere bóveda).",
        descripcion: "Ejecuta los escenarios de Pases Caja-Bóveda: validación del popup de error COE en pesos y dólares. Solo si el usuario tiene caja bóveda asignada.",
        resultado: "Todas las pruebas del módulo cumplen el resultado esperado",
        evidencias: ["Informe HTML", "ZIP QA"]
      },
      "cheques-regresion": {
        num: "REG", tipo: "smoke",
        regresionModulo: true,
        titulo: "Regresión — Cheques (todo el módulo)",
        corto: "Depósito e interdepósito: conexión, felices, negativos y USD.",
        descripcion: "Ejecuta todos los escenarios del módulo Cheques: conexión COBIS, flujos felices, judiciales, casos negativos y depósitos en USD.",
        resultado: "Todas las pruebas del módulo cumplen el resultado esperado",
        evidencias: ["Informe HTML", "ZIP QA"]
      },
      "parametria-sc161-regresion": {
        num: "REG", tipo: "smoke",
        regresionModulo: true,
        titulo: "Regresión — Parametría SC-161 (automáticos)",
        corto: "Todos los CA/CN automáticos de Relación TRN–Perfil.",
        descripcion: "Ejecuta todos los casos automáticos del ticket SC-161 (acceso, autocomplete, alta, edición y negativos). Excluye casos manuales (TA, PDF, catálogo F5).",
        resultado: "Todas las pruebas automáticas del módulo cumplen el resultado esperado",
        evidencias: ["Informe HTML", "ZIP QA"]
      },
      "cierre-cuadre-regresion": {
        num: "REG", tipo: "smoke",
        regresionModulo: true,
        titulo: "Regresión — Cuadre y Cierre de Caja",
        corto: "Smoke + CA/CN del módulo (sin Parametría cajas).",
        descripcion: "Ejecuta smoke de cuadre/cierre, Eliminar Cierre de Caja, cierre sin diferencia COBIS, cierre aceptando popup COBIS, cierre con sobrante/faltante (sot1) y cancelación del popup.",
        resultado: "Todas las pruebas del módulo cumplen el resultado esperado",
        evidencias: ["Informe HTML", "ZIP QA"]
      },
      "cierre-cuadre-131-regresion": {
        num: "REG", tipo: "smoke",
        regresionModulo: true,
        titulo: "Regresión — Cuadre y Cierre de Caja (alias)",
        corto: "Alias histórico → usar cierre-cuadre-regresion.",
        descripcion: "Alias de cierre-cuadre-regresion.",
        resultado: "Todas las pruebas del módulo cumplen el resultado esperado",
        evidencias: ["Informe HTML", "ZIP QA"]
      },
      "mejoras-sc416-regresion": {
        num: "REG", tipo: "smoke",
        regresionModulo: true,
        titulo: "Regresión — Cierre forzado SC-416",
        corto: "Los tres tipos de caja: Compensada, Normal y MiniBóveda.",
        descripcion: "Ejecuta CA-01 (Compensada sin Nota), CA-02 (Normal con Nota) y CA-03 (MiniBóveda con Nota) del ticket SC-416 en una sola corrida.",
        resultado: "Todas las pruebas del módulo cumplen el resultado esperado",
        evidencias: ["Informe HTML", "ZIP QA"]
      },
      "smoke-pipeline": {
        num: "DIAG", tipo: "smoke",
        entregaQaExcel: false,
        titulo: "Comprobar build y detección de tests",
        corto: "Compila el proyecto y lista pruebas (sin abrir SOT).",
        descripcion: "Ejecuta run-SmokePipeline.ps1: dotnet build y dotnet test --list-tests. Sirve para confirmar que tu PC puede ejecutar automatizaciones antes de una corrida larga. Si termina en segundos con «Sin pruebas», revisar build o tags Gherkin.",
        tag: "@ParametriaCajasSmoke131 (solo list-tests)",
        resultado: "Al menos 1 test listado para el filtro smoke",
        script: "run-SmokePipeline.ps1", extraArgs: "",
        prerequisitos: [
          "Runner API en localhost:5050",
          ".NET SDK y proyecto AutomatizacionSOT compilable"
        ],
        evidencias: ["Log con conteo de tests listados (no capturas de SOT)"]
      }
    };
