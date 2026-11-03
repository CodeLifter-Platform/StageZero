import { useCallback, useEffect, useRef, useState } from 'react';
import { api } from '../../api/client';
import type { ObjectListing } from '../../api/types';
import { useToast } from '../../app/toast';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { Notice } from '../../components/Notice';
import { Button, Dialog, FileBrowser, FileDrop, Input, ProgressBar, describeRejections, formatBytes, type FileEntry, type RejectedFile } from '../../ui';

/** The largest single upload: R2's limit for one PUT. Bigger files need multipart, which StageZero doesn't do yet. */
export const MAX_UPLOAD_BYTES = 5 * 1024 * 1024 * 1024;

/** The FileBrowser path for a prefix ("" → "/", "photos/2026/" → "/photos/2026"). */
export function pathForPrefix(prefix: string): string {
  return '/' + prefix.replace(/\/$/, '');
}

/** The prefix for a FileBrowser path ("/" → "", "/photos/2026" → "photos/2026/"). */
export function prefixForPath(path: string): string {
  const trimmed = path.replace(/^\/+/, '').replace(/\/+$/, '');
  return trimmed ? trimmed + '/' : '';
}

/** Cloudflare's rule for a folder name, checked here so the dialog can say so first. The server checks it again. */
export function folderNameProblem(name: string): string | null {
  const n = name.trim();
  if (!n) return 'Folder name is required.';
  if (n.includes('/')) return 'One folder at a time: no slashes.';
  if (n === '.' || n === '..') return 'A folder cannot be named . or ..';
  return null;
}

interface Upload { id: number; name: string; size: number; fraction: number; state: 'uploading' | 'done' | 'failed'; error?: string }

/**
 * One folder of a bucket: the design system's FileBrowser over the objects API, a FileDrop
 * that streams each file through StageZero to R2, and the folder and delete actions. The
 * S3 credentials never reach this page; downloads are signed links the server hands out.
 */
export function FilesPanel({ bucket }: { bucket: string }) {
  const notify = useToast();
  const [prefix, setPrefix] = useState('');
  const [listing, setListing] = useState<ObjectListing | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<string[]>([]);
  const [uploads, setUploads] = useState<Upload[]>([]);
  const [rejected, setRejected] = useState<string[]>([]);
  const [newFolder, setNewFolder] = useState(false);
  const [folderName, setFolderName] = useState('');
  const [folderTouched, setFolderTouched] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [busy, setBusy] = useState(false);
  const nextId = useRef(1);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setListing(await api.storage.objects(bucket, prefix));
      setError(null);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setLoading(false);
    }
  }, [bucket, prefix]);

  useEffect(() => { void load(); }, [load]);

  const more = async () => {
    if (!listing?.cursor) return;
    setLoading(true);
    try {
      const next = await api.storage.objects(bucket, prefix, listing.cursor);
      setListing({ ...next, entries: [...listing.entries, ...next.entries] });
    } catch (err) {
      notify((err as Error).message, 'error');
    } finally {
      setLoading(false);
    }
  };

  const entries: FileEntry[] = (listing?.entries ?? []).map((e) => ({
    name: e.isFolder ? e.name.replace(/\/$/, '') : e.name,
    kind: e.isFolder ? 'dir' : 'file',
    size: e.size ?? undefined,
    modified: e.modified ?? undefined,
  }));

  const navigate = (path: string) => { setPrefix(prefixForPath(path)); setSelected([]); };

  const open = async (entry: FileEntry) => {
    try {
      const link = await api.storage.downloadUrl(bucket, prefix + entry.name);
      window.open(link.url, '_blank', 'noopener');
    } catch (err) {
      notify((err as Error).message, 'error');
    }
  };

  const startUploads = (files: File[]) => {
    setRejected([]);
    for (const file of files) {
      const id = nextId.current++;
      setUploads((u) => [...u, { id, name: file.name, size: file.size, fraction: 0, state: 'uploading' }]);
      void api.storage.uploadObject(bucket, prefix + file.name, file, (fraction) =>
        setUploads((u) => u.map((x) => (x.id === id ? { ...x, fraction } : x))))
        .then(() => {
          setUploads((u) => u.map((x) => (x.id === id ? { ...x, fraction: 1, state: 'done' } : x)));
          void load();
        })
        .catch((err: Error) => setUploads((u) => u.map((x) => (x.id === id ? { ...x, state: 'failed', error: err.message } : x))));
    }
  };

  const onReject = (items: RejectedFile[]) => setRejected(describeRejections(items, { maxSize: MAX_UPLOAD_BYTES }));

  const createFolder = async () => {
    setFolderTouched(true);
    const problem = folderNameProblem(folderName);
    if (problem) return;
    setBusy(true);
    try {
      await api.storage.createFolder(bucket, prefix + folderName.trim() + '/');
      notify(`Folder ${folderName.trim()} created`, 'success');
      setNewFolder(false); setFolderName(''); setFolderTouched(false);
      await load();
    } catch (err) {
      notify((err as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    const byName = new Map(entries.map((e) => [e.name, e]));
    const keys = selected.filter((n) => byName.get(n)?.kind !== 'dir').map((n) => prefix + n);
    const folders = selected.filter((n) => byName.get(n)?.kind === 'dir').map((n) => prefix + n + '/');
    setBusy(true);
    try {
      const result = await api.storage.deleteObjects(bucket, keys, folders);
      notify(`${result.deleted} object${result.deleted === 1 ? '' : 's'} deleted`, 'success');
      setConfirmDelete(false);
      setSelected([]);
      await load();
    } catch (err) {
      notify((err as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  const uploading = uploads.some((u) => u.state === 'uploading');
  const here = prefix ? prefix : `the root of ${bucket}`;
  const folderProblem = folderNameProblem(folderName);

  return (
    <div className="sz-stack">
      <p className="sz-small sz-muted">
        Double-click a file to download it, or a folder to open it. Select rows to delete them; a folder goes with
        everything in it.
      </p>

      <FileBrowser path={pathForPrefix(prefix)} entries={entries} onNavigate={navigate} onOpen={(e) => void open(e)}
        selected={selected} onSelectedChange={setSelected} loading={loading} maxHeight={420}
        error={error} onRetry={() => void load()} label={`Files in ${bucket}`}
        empty={listing && !listing.entries.length ? 'This folder is empty. Drop files below to fill it.' : 'This folder is empty'}
        toolbar={<>
          <Button variant="ghost" size="sm" onClick={() => void load()} disabled={loading}>Refresh</Button>
          <Button variant="ghost" size="sm" onClick={() => setNewFolder(true)} disabled={busy}>New folder</Button>
          <Button variant="ghost" size="sm" onClick={() => setConfirmDelete(true)} disabled={busy || selected.length === 0}>
            Delete{selected.length ? ` (${selected.length})` : ''}
          </Button>
        </>} />

      {listing?.cursor ? (
        <div className="sz-row">
          <Button variant="outline" size="sm" onClick={() => void more()} disabled={loading}>Show more</Button>
          <span className="sz-xs sz-muted">This folder holds more than one page.</span>
        </div>
      ) : null}

      <FileDrop size="sm" onFiles={startUploads} onReject={onReject} maxSize={MAX_UPLOAD_BYTES} busy={uploading} disabled={!!error}
        label={<>Drop files to upload into <span className="sz-mono">{here}</span>, or browse</>}
        hint="Any type, up to 5 GB each. A file with the same name replaces the one that is there."
        error={rejected.length ? rejected.join(' ') : undefined} />

      {uploads.length ? (
        <div className="sz-stack-sm" aria-label="Uploads">
          {uploads.map((u) => (
            <ProgressBar key={u.id} size="sm" value={u.state === 'uploading' && u.fraction === 0 ? null : Math.round(u.fraction * 100)}
              tone={u.state === 'failed' ? 'danger' : u.state === 'done' ? 'success' : 'default'}
              label={<span className="sz-mono">{u.name}</span>} meta={formatBytes(u.size)}
              status={u.state === 'failed' ? u.error : u.state === 'done' ? 'Uploaded' : 'Uploading…'} />
          ))}
          {!uploading ? <div className="sz-row"><Button variant="ghost" size="sm" onClick={() => setUploads([])}>Clear</Button></div> : null}
        </div>
      ) : null}

      <Dialog open={newFolder} onClose={busy ? undefined : () => setNewFolder(false)} title="New folder"
        footer={<>
          <Button variant="ghost" onClick={() => setNewFolder(false)} disabled={busy}>Cancel</Button>
          <Button onClick={() => void createFolder()} disabled={busy}>{busy ? 'Creating…' : 'Create folder'}</Button>
        </>}>
        <div className="sz-stack">
          <Input label="Folder name" placeholder="2026" value={folderName} disabled={busy} autoFocus
            onChange={(e) => setFolderName(e.target.value)} onBlur={() => setFolderTouched(true)}
            error={folderTouched && folderProblem ? folderProblem : undefined}
            hint={!folderTouched || !folderProblem ? `Created in ${here}.` : undefined} />
          <Notice tone="info">
            Object storage has no real folders. StageZero writes an empty marker object so the folder shows before it has files,
            the same convention the Cloudflare dashboard uses.
          </Notice>
        </div>
      </Dialog>

      <ConfirmDialog open={confirmDelete} busy={busy} title="Delete from bucket"
        message={`Delete ${selected.length} item${selected.length === 1 ? '' : 's'} from '${bucket}'? A folder goes with everything in it. There is no undo.`}
        onCancel={() => setConfirmDelete(false)} onConfirm={() => void remove()} />
    </div>
  );
}
