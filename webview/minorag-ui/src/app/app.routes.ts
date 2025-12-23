import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: 'chat', pathMatch: 'full' },
  {
    path: 'chat',
    loadComponent: () => import('./chat/chat').then((m) => m.Chat),
  },
  {
    path: 'doctor',
    loadComponent: () => import('./doctor/doctor').then((m) => m.Doctor),
  },
];
