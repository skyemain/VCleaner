package com.skyemain.clenzy;
import android.database.Cursor;
import android.database.MatrixCursor;
import android.os.CancellationSignal;
import android.os.ParcelFileDescriptor;
import android.provider.DocumentsProvider;
import android.provider.DocumentsContract;
import java.io.*;

public class FixtureDocumentsProvider extends DocumentsProvider {
    private static final String[] COLUMNS = {"document_id","_display_name","mime_type","_size","last_modified","flags"};
    private File root() { return new File(getContext().getFilesDir(), "provider-fixtures"); }
    private File file(String id) throws FileNotFoundException {
        if (id.equals("root")) return root();
        if (!id.matches("[a-z]+\\.txt")) throw new FileNotFoundException();
        return new File(root(), id);
    }
    @Override public boolean isChildDocument(String parent, String child) { return parent.equals("root") && child.matches("[a-z]+\\.txt"); }
    @Override public android.os.Bundle call(String method, String arg, android.os.Bundle extras) {
        android.os.Bundle platformResult = super.call(method, arg, extras);
        if (platformResult != null) return platformResult;
        android.os.Bundle state = new android.os.Bundle();
        File a = new File(root(), "a.txt"), b = new File(root(), "b.txt");
        try {
            if (method.equals("change")) {
                java.nio.file.Files.write(b.toPath(), "changed content".getBytes(java.nio.charset.StandardCharsets.UTF_8));
            } else if (method.equals("clear")) { a.delete(); b.delete(); root().delete(); }
        } catch (IOException e) { throw new IllegalStateException(e); }
        state.putBoolean("a", a.exists()); state.putBoolean("b", b.exists());
        return state;
    }
    public boolean onCreate() {
        root().mkdirs();
        try {
            java.nio.file.Files.write(new File(root(), "a.txt").toPath(), "same bytes".getBytes(java.nio.charset.StandardCharsets.UTF_8));
            java.nio.file.Files.write(new File(root(), "b.txt").toPath(), "same bytes".getBytes(java.nio.charset.StandardCharsets.UTF_8));
            return true;
        } catch (IOException e) { throw new IllegalStateException("Fixture initialization failed", e); }
    }
    public Cursor queryRoots(String[] projection) {
        MatrixCursor c = new MatrixCursor(new String[]{"root_id","document_id","title","flags","mime_types","available_bytes"});
        c.addRow(new Object[]{"root", "root", "Clenzy test files", DocumentsContract.Root.FLAG_SUPPORTS_IS_CHILD, "*/*", 1048576L});
        return c;
    }
    private void add(MatrixCursor c, File f) {
        MatrixCursor.RowBuilder row = c.newRow();
        for (String name : c.getColumnNames()) {
            row.add(switch(name) {
                case "document_id" -> f.equals(root()) ? "root" : f.getName();
                case "_display_name" -> f.getName();
                case "mime_type" -> f.isDirectory() ? DocumentsContract.Document.MIME_TYPE_DIR : "text/plain";
                case "_size" -> f.length();
                case "last_modified" -> f.lastModified();
                case "flags" -> DocumentsContract.Document.FLAG_SUPPORTS_DELETE;
                default -> null;
            });
        }
    }
    public Cursor queryDocument(String id, String[] projection) throws FileNotFoundException { File f=file(id); if (!f.exists()) throw new FileNotFoundException(); MatrixCursor c=new MatrixCursor(projection==null?COLUMNS:projection); add(c,f); return c; }
    public Cursor queryChildDocuments(String id, String[] projection, String sortOrder) throws FileNotFoundException { MatrixCursor c=new MatrixCursor(projection==null?COLUMNS:projection); File[] files=file(id).listFiles(); if(files!=null) for(File f:files)add(c,f); return c; }
    public ParcelFileDescriptor openDocument(String id, String mode, CancellationSignal signal) throws FileNotFoundException { return ParcelFileDescriptor.open(file(id), ParcelFileDescriptor.MODE_READ_ONLY); }
    public void deleteDocument(String id) throws FileNotFoundException { File f=file(id); if(f.isDirectory() || !f.delete()) throw new FileNotFoundException(); }
}
