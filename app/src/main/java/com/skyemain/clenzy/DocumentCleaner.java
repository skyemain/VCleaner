package com.skyemain.clenzy;

import android.content.ContentResolver;
import android.database.Cursor;
import android.net.Uri;
import android.os.CancellationSignal;
import android.provider.DocumentsContract;
import java.io.*;
import java.security.MessageDigest;
import java.util.*;

public final class DocumentCleaner {
    public java.util.function.IntConsumer progress=count->{};
    private final ContentResolver resolver;
    private static final String[] COLUMNS = {
        DocumentsContract.Document.COLUMN_DOCUMENT_ID, DocumentsContract.Document.COLUMN_DISPLAY_NAME,
        DocumentsContract.Document.COLUMN_MIME_TYPE, DocumentsContract.Document.COLUMN_SIZE,
        DocumentsContract.Document.COLUMN_LAST_MODIFIED, DocumentsContract.Document.COLUMN_FLAGS
    };
    public record Scan(List<ReviewFile> files, int skipped, boolean truncated, int hashed, int notHashed) {}
    public record Outcome(int deleted, int skipped, long bytes) {}
    private record Metadata(String id, String name, String type, long bytes, long modified, int flags) {}
    private final Map<String, Metadata> observed = new HashMap<>();
    private Uri tree;
    private long hashLimit = 64L * 1024 * 1024;
    private int skipped;
    private boolean truncated;
    private int entries;

    public DocumentCleaner(ContentResolver resolver) { this.resolver = resolver; }

    public Scan scan(Uri selectedTree, CancellationSignal cancel) throws Exception { return scan(selectedTree, cancel, false); }
    public Scan scan(Uri selectedTree, CancellationSignal cancel, boolean deep) throws Exception {
        tree = selectedTree;
        observed.clear(); skipped = 0; truncated = false; entries = 0;
        List<ReviewFile> files = new ArrayList<>();
        walk(DocumentsContract.getTreeDocumentId(tree), 0, new HashSet<>(), files, cancel);
        Map<Long, Integer> sizes = new HashMap<>();
        for (ReviewFile f : files) if (f.bytes() > 0) sizes.merge(f.bytes(), 1, Integer::sum);
        List<ReviewFile> measured = new ArrayList<>();
        long hashBudget = (deep ? 4096L : 512L) * 1024 * 1024;
        hashLimit = (deep ? 1024L : 64L) * 1024 * 1024;
        int hashed = 0;
        for (ReviewFile file : files) {
            cancel.throwIfCanceled();
            String hash = null;
            if (file.bytes() > 0 && file.bytes() <= hashLimit && file.bytes() <= hashBudget && sizes.get(file.bytes()) > 1) {
                try {
                    hashBudget -= file.bytes();
                    hash = hash(Uri.parse(file.uri()), cancel);
                    Metadata after = query(Uri.parse(file.uri()));
                    if (!file.matches(after.bytes, after.modified)) hash = null;
                } catch (Exception e) { if (cancel.isCanceled()) throw e; skipped++; }
            }
            if (hash != null) hashed++;
            measured.add(new ReviewFile(file.uri(), file.name(), file.bytes(), file.modified(), hash));
        }
        return new Scan(List.copyOf(measured), skipped, truncated, hashed, files.size() - hashed);
    }

    private void walk(String id, int depth, Set<String> visited, List<ReviewFile> files, CancellationSignal cancel) throws Exception {
        cancel.throwIfCanceled();
        if (depth > 12 || files.size() >= 5000 || visited.size() >= 5000) { truncated = true; return; }
        if (!visited.add(id)) return;
        Uri children = DocumentsContract.buildChildDocumentsUriUsingTree(tree, id);
        try (Cursor cursor = resolver.query(children, COLUMNS, null, null, null, cancel)) {
            if (cursor == null) { skipped++; return; }
            while (cursor.moveToNext()) {
                cancel.throwIfCanceled();
                if(entries%50==0)progress.accept(entries);
                if (++entries > 5000) { truncated = true; break; }
                Metadata m = metadata(cursor);
                if (DocumentsContract.Document.MIME_TYPE_DIR.equals(m.type)) {
                    try { walk(m.id, depth + 1, visited, files, cancel); }
                    catch (Exception e) { if (cancel.isCanceled()) throw e; skipped++; }
                } else {
                    Uri uri = DocumentsContract.buildDocumentUriUsingTree(tree, m.id);
                    observed.put(uri.toString(), m);
                    files.add(new ReviewFile(uri.toString(), m.name, m.bytes, m.modified, null));
                }
            }
        }
    }

    public Outcome delete(List<ReviewFile> selected, CancellationSignal cancel) {
        int deleted = 0, skipped = 0;
        long bytes = 0;
        for (ReviewFile file : selected) {
            if (cancel.isCanceled()) break;
            try {
                if (tree == null || !observed.containsKey(file.uri())) { skipped++; continue; }
                Uri uri = Uri.parse(file.uri());
                Metadata now = query(uri);
                if (DocumentsContract.Document.MIME_TYPE_DIR.equals(now.type) || !file.matches(now.bytes, now.modified)
                    || (now.flags & DocumentsContract.Document.FLAG_SUPPORTS_DELETE) == 0) { skipped++; continue; }
                if (file.digest() != null && !file.digest().equals(hash(uri, cancel))) { skipped++; continue; }
                Metadata last = query(uri);
                if (!file.matches(last.bytes, last.modified)) { skipped++; continue; }
                if (DocumentsContract.deleteDocument(resolver, uri) && gone(uri)) {
                    deleted++; bytes += file.bytes(); observed.remove(file.uri());
                } else skipped++;
            } catch (Exception e) { skipped++; }
        }
        return new Outcome(deleted, skipped, bytes);
    }

    private boolean gone(Uri uri) {
        try(Cursor c=resolver.query(uri,new String[]{DocumentsContract.Document.COLUMN_DOCUMENT_ID},null,null,null)) {
            return c!=null && !c.moveToFirst();
        }catch(Exception e){return e instanceof java.io.FileNotFoundException;}
    }
    private Metadata query(Uri uri) throws IOException {
        try (Cursor c = resolver.query(uri, COLUMNS, null, null, null)) {
            if (c == null || !c.moveToFirst()) throw new IOException("Document unavailable");
            return metadata(c);
        }
    }
    private Metadata metadata(Cursor c) {
        return new Metadata(c.getString(0), c.getString(1), c.getString(2), c.isNull(3) ? -1 : c.getLong(3), c.isNull(4) ? 0 : c.getLong(4), c.getInt(5));
    }
    private String hash(Uri uri, CancellationSignal cancel) throws Exception {
        MessageDigest digest = MessageDigest.getInstance("SHA-256");
        try (InputStream stream = resolver.openInputStream(uri)) {
            if (stream == null) throw new IOException("Unreadable file");
            byte[] buffer = new byte[64 * 1024]; int count; long read = 0;
            while ((count = stream.read(buffer)) != -1) { cancel.throwIfCanceled(); read += count; if (read > hashLimit) throw new IOException("File grew beyond hash limit"); digest.update(buffer, 0, count); }
        }
        StringBuilder text = new StringBuilder();
        for (byte b : digest.digest()) text.append(String.format(Locale.ROOT, "%02x", b & 255));
        return text.toString();
    }
}
