# Runner Operador — Frontend Angular

UI del Runner SOT (Angular 19 + TypeScript).

## Requisitos

- Node.js 20+ (o el portable en `../.tools/node/...` que usa `Iniciar-Runner.bat`)
- npm

## Comandos

```bash
npm install
npm run build
```

El build publica en `../wwwroot/app` (servido por la API en `http://localhost:5050/`).

## Desarrollo

```bash
npm start
```

Apunta el proxy o usá la API en 5050. En producción el `.bat` compila Angular y levanta la API juntos.

## Legacy

El HTML monolítico anterior está en `../wwwroot/_legacy/mi-runner-operador.html` como respaldo.
