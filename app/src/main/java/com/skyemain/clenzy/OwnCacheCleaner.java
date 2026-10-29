package com.skyemain.clenzy;
import java.io.*;
import java.util.*;
import android.os.CancellationSignal;
public final class OwnCacheCleaner {
    private final File root;
    private final Map<String,ReviewFile> scanned=new HashMap<>();
    public OwnCacheCleaner(File root) {try {this.root=root.getCanonicalFile();}catch(IOException e){throw new IllegalArgumentException("Cache directory unavailable",e);}}
    public List<ReviewFile> scan(CancellationSignal cancel) throws IOException {
        scanned.clear(); List<ReviewFile> result=new ArrayList<>(); ArrayDeque<File> pending=new ArrayDeque<>(); pending.push(root);
        String base=root.getCanonicalPath()+File.separator; int examined=0;
        while(!pending.isEmpty() && examined++<5000) {
            cancel.throwIfCanceled(); File dir=pending.pop(); File[] entries=dir.listFiles(); if(entries==null) continue;
            for(File f:entries) {cancel.throwIfCanceled(); if(!f.getCanonicalPath().startsWith(base) || !f.getAbsolutePath().equals(f.getCanonicalPath())) continue;
                if(f.isDirectory()) pending.push(f);
                else if(f.isFile() && System.currentTimeMillis()-f.lastModified()>86400000L) {
                    ReviewFile item=new ReviewFile(f.toURI().toString(),f.getName(),f.length(),f.lastModified(),null); scanned.put(item.uri(),item); result.add(item);
                }
                if(result.size()>=5000) return result;
            }
        } return result;
    }
    public DocumentCleaner.Outcome delete(List<ReviewFile> selected,CancellationSignal cancel) {
        int deleted=0,skipped=0;long bytes=0;
        for(ReviewFile item:selected) {if(cancel.isCanceled()) break;
            try {File f=new File(java.net.URI.create(item.uri())); ReviewFile original=scanned.get(item.uri());
                if(original==null || !original.equals(item) || !f.getCanonicalPath().startsWith(root.getCanonicalPath()+File.separator) || !f.getAbsolutePath().equals(f.getCanonicalPath()) || !item.matches(f.length(),f.lastModified()) || !f.isFile()) {skipped++;continue;}
                if(f.delete() && !f.exists()) {deleted++;bytes+=item.bytes();} else skipped++;
            }catch(Exception e){skipped++;}
        }return new DocumentCleaner.Outcome(deleted,skipped,bytes);
    }
}
