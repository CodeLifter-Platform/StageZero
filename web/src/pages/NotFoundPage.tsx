import { useNavigate } from 'react-router-dom';
import { AnonymousFrame } from '../app/Shell';
import { useDocumentTitle } from '../components/useDocumentTitle';
import { Button, Card } from '../ui';

export function NotFoundPage() {
  useDocumentTitle('Not found');
  const navigate = useNavigate();
  return (
    <AnonymousFrame>
      <Card>
        <div className="sz-stack">
          <h1>Page not found</h1>
          <p className="sz-muted">Sorry, the page you're looking for doesn't exist.</p>
          <Button onClick={() => navigate('/')}>Go home</Button>
        </div>
      </Card>
    </AnonymousFrame>
  );
}
