package com.skyemain.clenzy;

import android.app.*;
import android.net.Uri;
import android.os.*;
import android.provider.MediaStore;
import androidx.lifecycle.*;
import java.util.*;
import java.util.concurrent.*;

public final class CleanupViewModel extends AndroidViewModel {
    public final MutableLiveData<Integer> updates = new MutableLiveData<>(0);
    public List<ReviewFile> files = List.of();
    public final Set<String> selected = new HashSet<>();
    public Uri tree;
    public boolean busy, media, deep, ownCache, completed, waitingSettings;
    public boolean movedToTrash;
    private List<Uri> trashUris=List.of();
    public long recoveredBytes; public int removedFiles, skippedFiles;
    public String status = "Choose Photos & Videos or a folder. Nothing is selected automatically.";
    public ReviewRules.Category category = ReviewRules.Category.LARGE;
    public PendingIntent pendingTrash;
    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private final Handler main = new Handler(Looper.getMainLooper());
    private final DocumentCleaner documents;
    private final MediaCleaner photos;
    private final OwnCacheCleaner cache;
    private CancellationSignal cancellation;
    public CleanupViewModel(Application app) {
        super(app); cache=new OwnCacheCleaner(app.getCacheDir()); documents = new DocumentCleaner(app.getContentResolver()); photos = new MediaCleaner(app.getContentResolver());
        documents.progress=count->main.post(()->{status="Examining authorized files · "+count+" entries inspected";changed();});
        photos.progress=count->main.post(()->{status="Examining authorized media · "+count+" items inspected";changed();});
        String saved = app.getSharedPreferences("cleanup", 0).getString("folder", null);
        if (saved != null) tree = Uri.parse(saved);
    }
    public void changed() { updates.setValue(updates.getValue() + 1); }
    public void folder(Uri uri) {
        tree = uri; media = false; ownCache=false;
        getApplication().getSharedPreferences("cleanup", 0).edit().putString("folder", uri.toString()).apply();
        scan(false);
    }
    public void scan(boolean deepScan) {
        if (busy) return;
        if (!ownCache && !media && tree == null) { status = "Choose a folder first."; changed(); return; }
        completed=false;movedToTrash=false; deep = deepScan; busy = true; selected.clear(); files = List.of(); cancellation = new CancellationSignal();
        status = "Scanning " + (media ? "authorized photos and videos" : "chosen folder") + " · " + (deep ? "deep scan, up to 4 GiB read" : "up to 512 MiB read") + "."; changed();
        worker.execute(() -> {
            try {
                List<ReviewFile> result; int hashed, notHashed; boolean truncated; int skipped = 0;
                if(ownCache) {result=cache.scan(cancellation);hashed=0;notHashed=result.size();truncated=result.size()>=5000;} else if (media) {
                    var scan = photos.scan(cancellation, deep); result = scan.files(); hashed = scan.hashed(); notHashed = scan.notHashed(); truncated = scan.truncated();
                } else {
                    var scan = documents.scan(tree, cancellation, deep); result = scan.files(); hashed = scan.hashed(); notHashed = scan.notHashed(); truncated = scan.truncated(); skipped = scan.skipped();
                }
                String message = result.size()+" files reviewed · "+hashed+" duplicate checks · "+notHashed+" not hashed (unique size, limit or unavailable) · "+skipped+" unreadable checks."+(truncated ? " Scan limit reached." : "");
                main.post(() -> { files = result; status = message; if(ownCache) for(ReviewFile f:files) selected.add(f.uri()); busy = false; changed(); });
            } catch (Exception e) { main.post(() -> { status = cancellation.isCanceled() ? "Scan cancelled. Nothing deleted." : "Access unavailable. Choose the folder or grant Photos access again."; busy = false; changed(); }); }
        });
    }
    public void cancel() { if (cancellation != null) cancellation.cancel(); }
    public List<ReviewFile> chosen() { List<ReviewFile> result = new ArrayList<>(); for (ReviewFile f : files) if (selected.contains(f.uri())) result.add(f); return result; }
    public void delete() {
        if (busy || selected.isEmpty()) return;
        List<ReviewFile> chosen = chosen(); busy = true; cancellation = new CancellationSignal(); changed();
        worker.execute(() -> {
            try {
                if (media) {
                    if (Build.VERSION.SDK_INT < 30) {
                        main.post(() -> { status = "Open the selected media in your gallery to remove it on this Android version."; busy = false; changed(); }); return;
                    }
                    List<Uri> eligible = photos.eligible(chosen, cancellation, deep);
                    if (eligible.isEmpty()) { main.post(() -> { status = "Selected media changed or is unavailable. Scan again."; busy = false; changed(); }); return; }
                    if (eligible.size() > 200) { main.post(() -> { status = "Choose up to 200 media items per Trash request."; busy = false; changed(); }); return; }
                    PendingIntent request = MediaStore.createTrashRequest(getApplication().getContentResolver(), eligible, true);
                    main.post(() -> { trashUris=List.copyOf(eligible);pendingTrash = request; status = "Review Android's Trash confirmation. "+(chosen.size()-eligible.size())+" changed/unavailable items skipped."; busy = false; changed(); });
                } else {
                    var result = ownCache ? cache.delete(chosen,cancellation) : documents.delete(chosen, cancellation);
                    main.post(() -> { completed=true; recoveredBytes=result.bytes(); removedFiles=result.deleted(); skippedFiles=result.skipped();
                        recordHistory(result.bytes(),result.deleted(),result.skipped());
                        status = "Deleted "+result.deleted()+" files · "+result.bytes()+" source bytes · "+result.skipped()+" skipped. Device free space may differ."; files = List.of(); selected.clear(); busy = false; changed(); });
                }
            } catch (Exception e) { main.post(() -> { status = "Operation stopped. Scan again to verify remaining items."; busy = false; changed(); }); }
        });
    }
    private void recordHistory(long bytes,int count,int skipped) {
        var prefs=getApplication().getSharedPreferences("cleanup",0);
        try {
            org.json.JSONArray old=new org.json.JSONArray(prefs.getString("history","[]"));
            org.json.JSONArray next=new org.json.JSONArray();next.put(new org.json.JSONObject().put("time",System.currentTimeMillis()).put("bytes",bytes).put("files",count).put("skipped",skipped));
            for(int n=0;n<Math.min(19,old.length());n++)next.put(old.get(n));
            prefs.edit().putString("history",next.toString()).putLong("lastClean",System.currentTimeMillis()).putLong("lastBytes",bytes).putInt("lastFiles",count).apply();
        }catch(org.json.JSONException ignored) {status += " History could not be saved.";}
    }
    public void trashResult(boolean accepted) {
        pendingTrash=null;
        if(!accepted){status="Trash request cancelled. No cleanup was confirmed.";changed();return;}
        busy=true;status="Verifying Android Trash results…";changed();
        List<Uri> requested=trashUris;
        worker.execute(()->{
            int verified=0;
            for(Uri uri:requested) {
                try(var cursor=getApplication().getContentResolver().query(uri,new String[]{"is_trashed"},null,null,null)) {
                    if(cursor!=null && cursor.moveToFirst() && cursor.getInt(0)==1)verified++;
                }catch(Exception ignored){}
            }
            final int count=verified;
            main.post(()->{movedToTrash=true;completed=true;removedFiles=count;skippedFiles=requested.size()-count;recoveredBytes=0;selected.clear();files=List.of();busy=false;status=count+" items verified in Trash. Free space may not increase until Trash is emptied.";changed();});
        });
    }
    @Override protected void onCleared() { cancel(); worker.shutdownNow(); }
}
