import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

/** Campo contraseña con Ver/Ocultar o ojito (holdToReveal). */
@Component({
  selector: 'app-password-field',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './password-field.component.html',
  styleUrl: './password-field.component.scss'
})
export class PasswordFieldComponent {
  @Input() label = '';
  @Input() value = '';
  @Output() valueChange = new EventEmitter<string>();
  @Input() placeholder = '';
  @Input() autocomplete = 'new-password';
  @Input() hint = '';
  @Input() hintSet = false;
  @Input() warnHint = '';
  @Input() name = '';
  /** true = mostrar solo mientras se mantiene el ojito. */
  @Input() holdToReveal = false;
  @Output() enter = new EventEmitter<void>();

  visible = false;

  toggle(): void {
    this.visible = !this.visible;
  }

  mostrar(): void {
    this.visible = true;
  }

  ocultar(): void {
    this.visible = false;
  }

  onTeclaMostrar(ev: KeyboardEvent): void {
    if (ev.key !== ' ' && ev.key !== 'Enter') return;
    ev.preventDefault();
    this.mostrar();
  }

  onInput(v: string): void {
    this.value = v;
    this.valueChange.emit(v);
  }
}
