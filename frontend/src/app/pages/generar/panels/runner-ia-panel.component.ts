import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-runner-ia-panel',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './runner-ia-panel.component.html',
  styleUrl: './runner-ia-panel.component.scss'
})
export class RunnerIaPanelComponent {
  @Input() open = false;
  @Input() proyectoNombre = '';
  @Input() proyectoActivoId = '';
  @Input() proyectos: Array<{ id: string; nombre: string; descripcion?: string }> = [];
  @Input() checklist: Array<{ id: string; texto: string }> = [];
  @Input() iaAlcanceTexto = '';
  @Input() descripcionProyecto = '';
  @Input() guardando = false;
  @Input() mapaHint = '';
  @Input() status = '';
  @Input() statusTipo = 'info';

  @Output() openChange = new EventEmitter<boolean>();
  @Output() proyectoChange = new EventEmitter<string>();
  @Output() reentrenar = new EventEmitter<void>();
  @Output() nuevoProyecto = new EventEmitter<void>();
  @Output() modificarProyecto = new EventEmitter<void>();
  @Output() eliminarProyecto = new EventEmitter<void>();

  toggle(ev: Event): void {
    ev.preventDefault();
    this.openChange.emit(!this.open);
  }

  onProyecto(id: string): void {
    this.proyectoActivoId = id;
    this.proyectoChange.emit(id);
  }
}
