import { Routes } from '@angular/router';
import { ShellComponent } from './layout/shell.component';

export const routes: Routes = [
  {
    path: '',
    component: ShellComponent,
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'runner' },
      { path: 'runner-ia', redirectTo: 'generar', pathMatch: 'full' },
      {
        path: 'runner',
        loadComponent: () =>
          import('./pages/runner/runner-page.component').then((m) => m.RunnerPageComponent)
      },
      {
        path: 'generar',
        loadComponent: () =>
          import('./pages/generar/generar-page.component').then((m) => m.GenerarPageComponent)
      },
      { path: 'mapeo', redirectTo: 'runner', pathMatch: 'full' },
      {
        path: 'config',
        loadComponent: () =>
          import('./pages/config/config-page.component').then((m) => m.ConfigPageComponent)
      },
      {
        path: 'ayuda',
        loadComponent: () =>
          import('./pages/ayuda/ayuda-page.component').then((m) => m.AyudaPageComponent)
      }
    ]
  },
  { path: '**', redirectTo: 'runner' }
];
