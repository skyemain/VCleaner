package com.skyemain.clenzy;

import android.os.CancellationSignal;
import java.io.*;
import java.nio.file.*;
import java.security.MessageDigest;
import java.util.*;

/** Local shared storage only. Never follows links or deletes directories. */
public final class SharedStorageCleaner {
    private final File root;
    private final Map<String,ReviewFile> scanned=new HashMap<>();
    public java.util.function.IntConsumer progress=count->{};
    public record Scan(List<ReviewFile> files,int hashed,int notHashed,boolean truncated) {}
    public SharedStorageCleaner(File root) {try{this.root=root.getCanonicalFile();}catch(IOException e){throw new IllegalArgumentException(e);}}
    private boolean allowed(File file) throws IOException {
        String path=file.getAbsolutePath(),base=root.getPath()+File.separator;
        if(!path.startsWith(base)||!path.equals(file.getCanonicalPath()))return false;
        String relative=path.substring(base.length());
        if(relative.equals("Android")||relative.startsWith("Android/"))return relative.startsWith("Android/media/")||relative.equals("Android/media");
        return !relative.startsWith(".Trash/")&&!relative.startsWith(".trash/");
    }
    public Scan scan(CancellationSignal cancel,boolean duplicates,boolean downloads) throws Exception {
        scanned.clear();List<ReviewFile> result=new ArrayList<>();int examined=0;
        ArrayDeque<File> pending=new ArrayDeque<>();pending.push(downloads?new File(root,"Download"):root);
        while(!pending.isEmpty()&&examined<100000){
            cancel.throwIfCanceled();File directory=pending.pop();
            if(!directory.equals(root)&&!allowed(directory))continue;
            try(var stream=Files.newDirectoryStream(directory.toPath())){
                for(Path path:stream){cancel.throwIfCanceled();if(++examined>100000)break;
                    File f=path.toFile();
                    if(examined%100==0)progress.accept(examined);
                    if(f.equals(new File(root,"Android"))){pending.push(new File(f,"media"));continue;}
                    if(!allowed(f))continue;
                    if(Files.isDirectory(path,LinkOption.NOFOLLOW_LINKS))pending.push(f);
                    else if(Files.isRegularFile(path,LinkOption.NOFOLLOW_LINKS))result.add(new ReviewFile(f.toURI().toString(),f.getName(),f.length(),f.lastModified(),null));
                }
            }catch(IOException|SecurityException ignored){}
        }
        Map<Long,Integer> sizes=new HashMap<>();for(ReviewFile f:result)sizes.merge(f.bytes(),1,Integer::sum);
        int hashed=0;long budget=16L*1024*1024*1024;
        for(int n=0;n<result.size();n++){
            cancel.throwIfCanceled();ReviewFile f=result.get(n);
            if(duplicates&&f.bytes()>0&&sizes.get(f.bytes())>1&&f.bytes()<=budget){
                budget-=f.bytes();try{File local=new File(java.net.URI.create(f.uri()));String hash=digest(local,cancel);
                    if(f.matches(local.length(),local.lastModified())){result.set(n,new ReviewFile(f.uri(),f.name(),f.bytes(),f.modified(),hash));hashed++;}
                }catch(IOException|SecurityException ignored){}
            }
        }
        for(ReviewFile f:result)scanned.put(f.uri(),f);
        return new Scan(result,hashed,result.size()-hashed,examined>=100000);
    }
    private String digest(File file,CancellationSignal cancel) throws Exception {
        MessageDigest hash=MessageDigest.getInstance("SHA-256");byte[] buffer=new byte[131072];
        long expected=file.length(),read=0;
        try(var input=new FileInputStream(file)){int count;while((count=input.read(buffer))!=-1){cancel.throwIfCanceled();read+=count;if(read>expected)throw new IOException("File changed during hashing");hash.update(buffer,0,count);}}
        if(read!=expected)throw new IOException("File changed during hashing");
        StringBuilder value=new StringBuilder();for(byte b:hash.digest())value.append(String.format("%02x",b&255));return value.toString();
    }
    public DocumentCleaner.Outcome delete(List<ReviewFile> selected,CancellationSignal cancel){
        int deleted=0,skipped=0;long bytes=0;
        Set<String> selectedUris=new HashSet<>();for(ReviewFile item:selected)selectedUris.add(item.uri());
        for(ReviewFile f:selected){if(cancel.isCanceled())break;
            try{File local=new File(java.net.URI.create(f.uri()));
                if(!f.equals(scanned.get(f.uri()))||!allowed(local)||!Files.isRegularFile(local.toPath(),LinkOption.NOFOLLOW_LINKS)||!f.matches(local.length(),local.lastModified())){skipped++;continue;}
                if(f.digest()!=null){
                    if(!f.digest().equals(digest(local,cancel))){skipped++;continue;}
                    boolean keeperExpected=false,keeperVerified=false;
                    for(ReviewFile keep:scanned.values())if(f.digest().equals(keep.digest())&&!selectedUris.contains(keep.uri())){
                        keeperExpected=true;File kept=new File(java.net.URI.create(keep.uri()));
                        if(allowed(kept)&&kept.isFile()&&keep.matches(kept.length(),kept.lastModified())&&keep.digest().equals(digest(kept,cancel))){keeperVerified=true;break;}
                    }
                    if(keeperExpected&&!keeperVerified){skipped++;continue;}
                }
                if(!allowed(local)||!f.matches(local.length(),local.lastModified())){skipped++;continue;}
                if(local.delete()&&!local.exists()){deleted++;bytes+=f.bytes();}else skipped++;
            }catch(Exception e){skipped++;}
        }
        return new DocumentCleaner.Outcome(deleted,skipped,bytes);
    }
}
