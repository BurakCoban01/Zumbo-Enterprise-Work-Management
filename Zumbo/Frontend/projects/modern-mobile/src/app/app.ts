import { Component } from '@angular/core';
import { IonApp, IonRouterOutlet } from '@ionic/angular/standalone';

@Component({
  selector: 'zumbo-root',
  imports: [IonApp, IonRouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
}
