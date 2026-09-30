import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'accounts' },
  { path: 'accounts', title: 'Relay Scorecard', loadComponent: () => import('./pages/portfolio-page').then(m => m.PortfolioPage) },
  { path: 'accounts/:id', title: 'Relay Scorecard · Account', loadComponent: () => import('./pages/account-page').then(m => m.AccountPage) },
  { path: '**', redirectTo: 'accounts' },
];
