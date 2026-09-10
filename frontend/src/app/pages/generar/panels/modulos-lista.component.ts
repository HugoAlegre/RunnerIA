import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ModuloListaItem, PruebaListaItem } from '../../../core/services/catalog.service';

@Component({
  selector: 'app-modulos-lista',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './modulos-lista.component.html',
  styleUrl: './modulos-lista.component.scss'
})
export class ModulosListaComponent {
  @Input() modulos: ModuloListaItem[] = [];
  @Input() abiertos: Record<string, boolean> = {};
  @Input() menuModuloId: string | null = null;
  @Input() renameId: string | null = null;
  @Input() renameValue = '';
  @Input() gherkinOpen: Record<string, string> = {};
  @Input() deshacerHint = '';
  @Input() puedeDeshacer = false;

  @Output() refresh = new EventEmitter<void>();
  @Output() deshacer = new EventEmitter<void>();
  @Output() toggleGrupo = new EventEmitter<string>();
  @Output() toggleMenu = new EventEmitter<{ id: string; ev: Event }>();
  @Output() agregarPrueba = new EventEmitter<ModuloListaItem>();
  @Output() abrirRename = new EventEmitter<{ m: ModuloListaItem; ev: Event }>();
  @Output() eliminarModulo = new EventEmitter<{ m: ModuloListaItem; ev: Event }>();
  @Output() renameValueChange = new EventEmitter<string>();
  @Output() confirmRename = new EventEmitter<{ m: ModuloListaItem; ev: Event }>();
  @Output() cancelRename = new EventEmitter<Event>();
  @Output() toggleGherkin = new EventEmitter<PruebaListaItem>();
  @Output() editarPrueba = new EventEmitter<PruebaListaItem>();
  @Output() eliminarPrueba = new EventEmitter<PruebaListaItem>();

  isOpen(id: string): boolean {
    return !!this.abiertos[id];
  }
}
