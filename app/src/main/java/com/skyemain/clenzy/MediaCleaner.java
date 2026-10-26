package com.skyemain.clenzy;

import android.content.*;
import android.database.Cursor;
import android.net.Uri;
import android.os.*;
import android.provider.MediaStore;
import java.io.*;
import java.security.MessageDigest;
import java.util.*;

public final class MediaCleaner {
    private final ContentResolver resolver;
    private final Map<String, ReviewFile> observed = new HashMap<>();
    public MediaCleaner(ContentResolver resolver) { this.resolver = resolver; }
    public record Scan(List<ReviewFile> files, int hashed, int notHashed, boolean truncated) {}
    public Scan scan(CancellationSignal cancel, boolean deep) throws Exception {
        observed.clear();
        List<ReviewFile> files = new ArrayList<>();
        boolean truncated = false; int readableCollections = 0;
        for (Uri collection : List.of(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, MediaStore.Video.Media.EXTERNAL_CONTENT_URI)) {
            try (Cursor c = resolver.query(collection, new String[]{"_id", "_display_name", "_size", "date_modified"}, null, null, "date_modified DESC", cancel)) {
                if (c == null) continue;
                readableCollections++;
                while (c.moveToNext()) {
                    cancel.throwIfCanceled();
                    if (files.size() >= 10000) { truncated = true; break; }
                    ReviewFile f = new ReviewFile(ContentUris.withAppendedId(collection, c.getLong(0)).toString(), c.getString(1), c.isNull(2) ? -1 : c.getLong(2), c.getLong(3) * 1000, null);
                    files.add(f); observed.put(f.uri(), f);
                }
            } catch (SecurityException e) { /* Images and videos can have different access grants. */ }
        }
        if (readableCollections == 0) throw new SecurityException("No media access");
        Map<Long,Integer> sizes = new HashMap<>();
        for (ReviewFile f : files) if (f.bytes() > 0) sizes.merge(f.bytes(), 1, Integer::sum);
        long budget = (deep ? 4096L : 512L) * 1024 * 1024;
        long limit = (deep ? 1024L : 64L) * 1024 * 1024;
        List<ReviewFile> result = new ArrayList<>(); int hashed = 0;
        for (ReviewFile f : files) {
            cancel.throwIfCanceled(); String digest = null;
            if (f.bytes() > 0 && f.bytes() <= limit && f.bytes() <= budget && sizes.get(f.bytes()) > 1) {
                budget -= f.bytes();
                try { digest = hash(Uri.parse(f.uri()), cancel, limit); if (!unchanged(f)) digest = null; }
                catch (IOException e) { digest = null; }
            }
            if (digest != null) hashed++;
            ReviewFile item = new ReviewFile(f.uri(), f.name(), f.bytes(), f.modified(), digest);
            result.add(item); observed.put(item.uri(), item);
        }
        return new Scan(List.copyOf(result), hashed, result.size() - hashed, truncated);
    }
    public List<Uri> eligible(List<ReviewFile> selected, CancellationSignal cancel, boolean deep) throws Exception {
        List<Uri> result = new ArrayList<>();
        for (ReviewFile f : selected) {
            cancel.throwIfCanceled();
            if (!f.equals(observed.get(f.uri()))) continue;
            try {
                if (!unchanged(f)) continue;
                if (f.digest() != null && !f.digest().equals(hash(Uri.parse(f.uri()), cancel, (deep ? 1024L : 64L) * 1024 * 1024))) continue;
                if (unchanged(f)) result.add(Uri.parse(f.uri()));
            } catch (IOException | SecurityException e) { /* Changed or unreadable media cannot be safely selected for deletion. */ }
        }
        return result;
    }
    private boolean unchanged(ReviewFile f) {
        try (Cursor c = resolver.query(Uri.parse(f.uri()), new String[]{"_size", "date_modified"}, null, null, null)) {
            return c != null && c.moveToFirst() && !c.isNull(0) && f.matches(c.getLong(0), c.getLong(1) * 1000);
        }
    }
    private String hash(Uri uri, CancellationSignal cancel, long limit) throws Exception {
        MessageDigest digest = MessageDigest.getInstance("SHA-256");
        try (InputStream stream = resolver.openInputStream(uri)) {
            if (stream == null) throw new IOException("Unavailable");
            byte[] buffer = new byte[65536]; int n; long read = 0;
            while ((n = stream.read(buffer)) != -1) {
                cancel.throwIfCanceled(); read += n; if (read > limit) throw new IOException("Limit exceeded");
                digest.update(buffer, 0, n);
            }
        }
        StringBuilder out = new StringBuilder();
        for (byte b : digest.digest()) out.append(String.format(Locale.ROOT, "%02x", b & 255));
        return out.toString();
    }
}
