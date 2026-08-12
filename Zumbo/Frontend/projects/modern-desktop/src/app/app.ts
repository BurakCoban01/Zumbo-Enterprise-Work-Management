import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { PwaUpdateService } from '@zumbo/modern-shared';

@Component({
  selector: 'zumbo-root',
  imports: [RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  protected readonly pwaUpdate = inject(PwaUpdateService);
}
