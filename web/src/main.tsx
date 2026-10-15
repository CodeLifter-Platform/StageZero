import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import './design/tokens/colors.css';
import './design/tokens/typography.css';
import './design/tokens/spacing.css';
import './design/tokens/effects.css';
import './theme/stagezero.css';
import './theme/app.css';
import { App } from './App';

const root = document.getElementById('root');
if (!root) throw new Error('index.html has no #root element');

createRoot(root).render(
  <StrictMode>
    <BrowserRouter>
      <App />
    </BrowserRouter>
  </StrictMode>,
);
