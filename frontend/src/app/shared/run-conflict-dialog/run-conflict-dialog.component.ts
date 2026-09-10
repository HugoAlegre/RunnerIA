import { Component, EventEmitter, HostListener, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RunConflictAction } from '../../core/models';

@Component({
  selector: 'app-run-conflict-dialog',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './run-conflict-dialog.component.html',
  styleUrl: './run-conflict-dialog.component.scss'
})
export class RunConflictDialogComponent {
  @Input() open = false;
  @Input() tituloActual = '';
  @Input() tituloNueva = '';

  @Output() closed = new EventEmitter<RunConflictAction>();

  @HostListener('document:keydown.escape')
  onEsc(): void {
    if (this.open) this.closed.emit('cancel');
  }

  onBackdrop(): void {
    this.closed.emit('cancel');
  }

  enqueue(): void {
    this.closed.emit('enqueue');
  }

  stopAndRun(): void {
    this.closed.emit('stop-and-run');
  }

  cancel(): void {
    this.closed.emit('cancel');
  }
}
