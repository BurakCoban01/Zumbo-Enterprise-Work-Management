import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { IonButton, IonContent, IonHeader, IonInput, IonItem, IonTitle, IonToolbar } from '@ionic/angular/standalone';
import { finalize } from 'rxjs';
import { normalizeApiError, ZumboSessionService } from '@zumbo/modern-shared';

@Component({
  selector: 'zumbo-mobile-login',
  imports: [ReactiveFormsModule, IonButton, IonContent, IonHeader, IonInput, IonItem, IonTitle, IonToolbar],
  templateUrl: './login.page.html',
  styleUrl: './login.page.scss'
})
export class MobileLoginPage {
  private readonly forms = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly session = inject(ZumboSessionService);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly mfaRequired = signal(false);
  protected readonly form = this.forms.nonNullable.group({
    usernameOrEmail: ['', Validators.required],
    password: ['', Validators.required],
    mfaCode: ['']
  });

  constructor() {
    if (this.session.authenticated()) void this.router.navigate(['/workspace']);
  }

  protected submit(): void {
    if (this.form.invalid || this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.session.login(this.form.getRawValue()).pipe(finalize(() => this.busy.set(false))).subscribe({
      next: () => void this.router.navigate(['/workspace']),
      error: value => {
        const error = normalizeApiError(value);
        this.mfaRequired.set(error.code === 'MFA_REQUIRED' || error.code === 'MFA_INVALID');
        this.error.set(this.mfaRequired() ? 'Doğrulama kodunu kontrol edin.' : 'Giriş bilgileri doğrulanamadı.');
      }
    });
  }
}
