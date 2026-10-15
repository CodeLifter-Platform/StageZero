import { Route, Routes } from 'react-router-dom';
import { AnonymousOnly, RequireOperator } from './app/guards';
import { SessionProvider } from './app/session';
import { ToastProvider } from './app/toast';
import { AccountSetupPage } from './pages/auth/AccountSetupPage';
import { ForgotPasswordPage } from './pages/auth/ForgotPasswordPage';
import { LoginPage } from './pages/auth/LoginPage';
import { ResetPasswordPage } from './pages/auth/ResetPasswordPage';
import { SetupPage } from './pages/auth/SetupPage';
import { DnsConfigPage } from './pages/dns/DnsConfigPage';
import { HomePage } from './pages/HomePage';
import { IpMonitorPage } from './pages/IpMonitorPage';
import { NotFoundPage } from './pages/NotFoundPage';
import { AccessStatusPage } from './pages/tunnel/AccessStatusPage';
import { TunnelRouteEditPage } from './pages/tunnel/TunnelRouteEditPage';
import { TunnelRoutesPage } from './pages/tunnel/TunnelRoutesPage';
import { TunnelSettingsPage } from './pages/tunnel/TunnelSettingsPage';

export function App() {
  return (
    <ToastProvider>
      <SessionProvider>
        <AppRoutes />
      </SessionProvider>
    </ToastProvider>
  );
}

export function AppRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<AnonymousOnly><LoginPage /></AnonymousOnly>} />
      <Route path="/setup" element={<AnonymousOnly allowFreshInstall><SetupPage /></AnonymousOnly>} />
      <Route path="/forgot-password" element={<AnonymousOnly><ForgotPasswordPage /></AnonymousOnly>} />
      <Route path="/reset-password" element={<AnonymousOnly><ResetPasswordPage /></AnonymousOnly>} />
      <Route path="/account-setup" element={<AccountSetupPage />} />

      <Route path="/" element={<RequireOperator><HomePage /></RequireOperator>} />
      <Route path="/ip-monitor" element={<RequireOperator><IpMonitorPage /></RequireOperator>} />
      <Route path="/dns-config" element={<RequireOperator><DnsConfigPage /></RequireOperator>} />
      <Route path="/tunnel-routes" element={<RequireOperator><TunnelRoutesPage /></RequireOperator>} />
      <Route path="/tunnel-routes/add" element={<RequireOperator><TunnelRouteEditPage /></RequireOperator>} />
      <Route path="/tunnel-routes/edit/:id" element={<RequireOperator><TunnelRouteEditPage /></RequireOperator>} />
      <Route path="/tunnel-routes/access/:id" element={<RequireOperator><AccessStatusPage /></RequireOperator>} />
      <Route path="/tunnel-settings" element={<RequireOperator><TunnelSettingsPage /></RequireOperator>} />
      <Route path="*" element={<NotFoundPage />} />
    </Routes>
  );
}
