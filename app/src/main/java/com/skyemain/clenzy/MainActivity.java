package com.skyemain.clenzy;

import android.Manifest;
import android.app.*;
import android.content.*;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.*;
import android.provider.Settings;
import android.text.format.Formatter;
import android.view.*;
import android.widget.*;
import androidx.activity.ComponentActivity;
import androidx.lifecycle.ViewModelProvider;
import androidx.recyclerview.widget.*;
import java.util.*;

public class MainActivity extends androidx.appcompat.app.AppCompatActivity {
    private int ink=Color.WHITE,background=Color.rgb(14,23,38),cardColor=Color.rgb(25,36,55);
    private int mint = Color.rgb(103, 154, 255), muted = Color.rgb(151, 179, 173);
    private CleanupViewModel model;
    private Uri tree;
    private TextView status, metrics, selection;
    private Button choose, photos, scan, deep, cancel, remove;
    private ProgressBar usage, progress;
    private ReviewAdapter adapter;
    private boolean launchingTrash;
    private boolean sortByName;
    private boolean sortByDate;
    private final java.util.concurrent.ExecutorService thumbnails = java.util.concurrent.Executors.newFixedThreadPool(2);
    private int dp(float value) { return Math.round(value * getResources().getDisplayMetrics().density); }

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        boolean dark=(getResources().getConfiguration().uiMode&48)==32;
        mint=Color.parseColor(dark?"#679AFF":"#3268E8");muted=Color.parseColor(dark?"#A7B3C9":"#647087");
        ink=Color.parseColor(dark?"#F3F6FF":"#172137");background=Color.parseColor(dark?"#0E1726":"#F3F6FC");cardColor=Color.parseColor(dark?"#192437":"#FFFFFF");
        model = new ViewModelProvider(this).get(CleanupViewModel.class); tree = model.tree;
        LinearLayout page = column(); page.setPadding(dp(22), dp(20), dp(22), dp(16));
        page.setBackground(new GradientDrawable(GradientDrawable.Orientation.TL_BR, new int[]{background,background}));
        page.setOnApplyWindowInsetsListener((v, insets) -> { v.setPadding(dp(22), insets.getSystemWindowInsetTop()+dp(12), dp(22), insets.getSystemWindowInsetBottom()+dp(12)); return insets; });
        TextView brand = label("✦  C L E N Z Y", 14, mint); brand.setTypeface(null, Typeface.BOLD); page.addView(brand);
        TextView title = label("Cleanup ready", 30, ink); title.setTypeface(null, Typeface.BOLD); page.addView(title);
        page.addView(label("Select what you no longer need. Your files stay in your control.", 14, muted));
        LinearLayout storage = column(); styleCard(storage);
        storage.addView(label("DEVICE STORAGE", 11, mint)); metrics = label("", 22, ink); storage.addView(metrics);
        usage = new ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal); usage.setProgressTintList(android.content.res.ColorStateList.valueOf(mint)); storage.addView(usage);
        page.addView(storage, spaced());
        LinearLayout sources = new LinearLayout(this);
        photos = button("Photos & Videos", this::mediaAccess); choose = button("Choose folder", this::chooseFolder);
        sources.addView(photos, new LinearLayout.LayoutParams(0,-2,1)); sources.addView(choose, new LinearLayout.LayoutParams(0,-2,1)); page.addView(sources);
        LinearLayout actions = new LinearLayout(this);
        scan = button("Scan", () -> model.scan(false)); deep = button("Deep scan", () -> new AlertDialog.Builder(this).setTitle("Deeper duplicate scan?").setMessage("Reads up to 4 GiB, with a 1 GiB per-file limit. Provider files may download and use data. You can cancel.").setNegativeButton("Cancel", null).setPositiveButton("Scan", (d,w) -> model.scan(true)).show());
        cancel = button("Cancel", model::cancel);
        actions.addView(scan); actions.addView(deep); actions.addView(cancel); page.addView(actions);
        progress = new ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal); progress.setIndeterminate(true); progress.setIndeterminateTintList(android.content.res.ColorStateList.valueOf(mint)); page.addView(progress);
        status = label("", 12, muted); status.setMaxLines(4); page.addView(status);
        Spinner picker = new Spinner(this);
        picker.setAdapter(new ArrayAdapter<>(this, android.R.layout.simple_spinner_dropdown_item, new String[]{"Large files · 100 MiB+", "Older than 30 days", "Exact duplicates", "Screenshots (name match)", "Videos", "All accessible files"}));
        picker.setSelection(model.category.ordinal());
        picker.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
            public void onItemSelected(AdapterView<?> p, View v, int position, long id) { model.category = ReviewRules.Category.values()[position]; model.changed(); }
            public void onNothingSelected(AdapterView<?> p) {}
        });
        LinearLayout filters = new LinearLayout(this); filters.addView(picker, new LinearLayout.LayoutParams(0,-2,1));
        Button sort = button("Sort", () -> new AlertDialog.Builder(this).setTitle("Sort files").setItems(new String[]{"Largest first", "Name", "Newest first"}, (d,which) -> { sortByName = which == 1; sortByDate=which==2; render(); }).show()); filters.addView(sort); page.addView(filters);
        RecyclerView list = new RecyclerView(this); list.setLayoutManager(new LinearLayoutManager(this)); adapter = new ReviewAdapter(); list.setAdapter(adapter);
        page.addView(list, new LinearLayout.LayoutParams(-1,0,1));
        LinearLayout selectionActions=new LinearLayout(this);
        selectionActions.addView(button("Select safe items",()->{if(model.ownCache && !model.busy) {for(ReviewFile f:model.files) model.selected.add(f.uri());render();}else new AlertDialog.Builder(this).setMessage("Personal files require individual selection. Only Clenzy’s own scanned cache is eligible for Select safe items.").setPositiveButton("Done",null).show();}));
        selectionActions.addView(button("Deselect all",()->{if(!model.busy){model.selected.clear();render();}}));page.addView(selectionActions);
        selection = label("", 12, muted); page.addView(selection);
        remove = button("CLEAN SELECTED", this::confirmDelete); page.addView(remove);
        TextView help = label("Private by design · only files you authorize", 11, muted); help.setGravity(Gravity.CENTER); help.setOnClickListener(v -> new AlertDialog.Builder(this).setTitle("Your files, your choice").setMessage("Clenzy cannot clear other apps' private caches or RAM. Photos use Android's Trash confirmation on Android 11+. Folder deletion may be permanent and may affect cloud copies. Age alone never means junk.").setPositiveButton("Done", null).setNeutralButton("Permissions", (d,w) -> startActivity(new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:"+getPackageName())))).show()); page.addView(help);
        launchingTrash = state != null && state.getBoolean("trashLaunched", false);
        setContentView(page);
        model.updates.observe(this, ignored -> render());
        String tool=getIntent().getStringExtra("tool");
        if(tool!=null) {
            sources.setVisibility(View.GONE); actions.setVisibility(View.GONE);
            model.category=tool.equals("large")?ReviewRules.Category.LARGE:tool.equals("duplicates")?ReviewRules.Category.DUPLICATES:ReviewRules.Category.ALL;
            picker.setSelection(model.category.ordinal());
            if(state==null) {
                if(tool.equals("cache")) {model.ownCache=true;model.media=false;model.scan(false);}
                else if(tool.equals("folder")||tool.equals("downloads")) explainFolder();
                else if(model.tree!=null && !tool.equals("photos")) {model.ownCache=false;model.media=false;model.scan(false);}
                else new AlertDialog.Builder(this).setTitle("Allow Clenzy to find files you can review").setMessage("Photos access covers only authorized images and videos. You can choose a folder instead for documents and downloads. Nothing personal is selected automatically.").setPositiveButton("Continue",(d,w)->mediaAccess()).setNeutralButton("Choose folder",(d,w)->chooseFolder()).setNegativeButton("Back",(d,w)->finish()).show();
            }
        }
    }
    @Override protected void onResume() {
        super.onResume();
        if(model!=null && model.waitingSettings) {
            model.waitingSettings=false;
            boolean allowed=Build.VERSION.SDK_INT>=33 ? checkSelfPermission(Manifest.permission.READ_MEDIA_IMAGES)==PackageManager.PERMISSION_GRANTED || checkSelfPermission(Manifest.permission.READ_MEDIA_VIDEO)==PackageManager.PERMISSION_GRANTED || (Build.VERSION.SDK_INT>=34 && checkSelfPermission(Manifest.permission.READ_MEDIA_VISUAL_USER_SELECTED)==PackageManager.PERMISSION_GRANTED) : checkSelfPermission(Manifest.permission.READ_EXTERNAL_STORAGE)==PackageManager.PERMISSION_GRANTED;
            if(allowed){model.ownCache=false;model.media=true;model.scan(false);}else{model.status="Access is still limited. Go back and choose a folder to continue.";model.changed();}
        }
    }
    private void explainFolder() {new AlertDialog.Builder(this).setTitle("Choose a folder to review").setMessage("Android grants access only to the folder you select. On newer Android versions, choose a supported subfolder or document provider. Clenzy cannot access Android/data or other apps’ private storage.").setPositiveButton("Continue",(d,w)->chooseFolder()).setNegativeButton("Back",(d,w)->finish()).show();}
    private void render() {
        if(model.completed && getIntent().hasExtra("tool")) {showResults();return;}
        tree = model.tree; status.setText(model.status); progress.setVisibility(model.busy ? View.VISIBLE : View.GONE); cancel.setVisibility(model.busy ? View.VISIBLE : View.GONE);
        if(getIntent().hasExtra("tool")) { ((View)cancel.getParent()).setVisibility(model.busy?View.VISIBLE:View.GONE); scan.setVisibility(View.GONE);deep.setVisibility(View.GONE); }
        for (Button b : List.of(choose, photos, scan, deep)) b.setEnabled(!model.busy && model.pendingTrash == null);
        List<ReviewFile> visible = ReviewRules.filter(model.files, model.category, System.currentTimeMillis());
        if (sortByName && model.category != ReviewRules.Category.DUPLICATES) visible.sort(Comparator.comparing(ReviewFile::name, String.CASE_INSENSITIVE_ORDER));
        if(sortByDate && model.category!=ReviewRules.Category.DUPLICATES)visible.sort(Comparator.comparingLong(ReviewFile::modified).reversed());
        adapter.submit(visible);
        updateSelection(); refreshMetrics();
        if (model.pendingTrash != null && !launchingTrash) {
            launchingTrash = true;
            try { startIntentSenderForResult(model.pendingTrash.getIntentSender(), 30, null, 0,0,0); }
            catch (IntentSender.SendIntentException e) { launchingTrash = false; model.trashResult(false); }
        }
    }
    private void updateSelection() {
        long bytes = ReviewRules.selectedBytes(model.files,model.selected);
        selection.setText(model.selected.isEmpty() ? "Nothing selected · tap Preview to inspect a file" : model.selected.size()+" selected · "+bytes(bytes)+" source size");
        remove.setBackgroundTintList(android.content.res.ColorStateList.valueOf(Color.rgb(50,104,232)));remove.setTextColor(Color.WHITE);
        remove.setText("CLEAN SELECTED — "+bytes(bytes)); remove.setEnabled(!model.busy && model.pendingTrash == null && !model.selected.isEmpty());
    }
    private void confirmDelete() {
        boolean all = ReviewRules.selectsEveryCopy(model.files, model.selected);
        new AlertDialog.Builder(this).setTitle(model.media ? "Move selected media to Trash?" : "Permanently delete selected files?")
            .setMessage((all ? "You selected EVERY copy in a duplicate group.\n\n" : "") + (model.media ? "Android will ask you to confirm. Trashed items may still use storage until removed." : "There may be no recycle bin. Cloud providers may delete synced copies too. Age does not mean a file is unnecessary."))
            .setNegativeButton("Cancel", null).setPositiveButton("Continue", (d,w) -> model.delete()).show();
    }
    private void showResults() {
        LinearLayout result=column();result.setPadding(dp(28),dp(32),dp(28),dp(24));result.setBackgroundColor(background);
        result.addView(label("✓",48,mint));result.addView(label("CLEANING COMPLETE",26,ink));
        result.addView(label(model.movedToTrash ? model.removedFiles+" items moved to Trash" : bytes(model.recoveredBytes)+" successfully deleted",30,ink));
        result.addView(label(model.removedFiles+(model.movedToTrash?" items trashed · ":" files removed · ")+model.skippedFiles+" skipped or unavailable",16,muted));
        StatFs disk=new StatFs(Environment.getDataDirectory().getAbsolutePath());result.addView(label(bytes(disk.getAvailableBytes())+" available now",18,muted));
        result.addView(label((model.movedToTrash ? "Trashed media may still use storage until the gallery empties Trash. " : "")+"Deletion totals reflect verified provider operations. Actual free space can differ because of provider trash, cloud storage and filesystem accounting.",13,muted));
        result.addView(button("DONE",this::finish));setContentView(result);
    }
    private void chooseFolder() {
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT_TREE);
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_WRITE_URI_PERMISSION | Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION);
        startActivityForResult(intent, 10);
    }
    private void mediaAccess() {
        model.ownCache=false;
        ArrayList<String> permissions = new ArrayList<>();
        if (Build.VERSION.SDK_INT >= 33) { permissions.add(Manifest.permission.READ_MEDIA_IMAGES); permissions.add(Manifest.permission.READ_MEDIA_VIDEO); if (Build.VERSION.SDK_INT >= 34) permissions.add(Manifest.permission.READ_MEDIA_VISUAL_USER_SELECTED); }
        else permissions.add(Manifest.permission.READ_EXTERNAL_STORAGE);
        boolean granted = true; for (String p : permissions) if (checkSelfPermission(p) != PackageManager.PERMISSION_GRANTED) granted = false;
        if (!granted) requestPermissions(permissions.toArray(new String[0]), 20); else { model.ownCache=false; model.media = true; model.scan(false); }
    }
    @Override public void onRequestPermissionsResult(int request, String[] permissions, int[] grants) {
        super.onRequestPermissionsResult(request, permissions, grants);
        if (request == 20) {
            boolean any = false; for (int g : grants) if (g == PackageManager.PERMISSION_GRANTED) any = true;
            if (any) { model.ownCache=false; model.media = true; model.scan(false); }
            else {
                model.status = "Photos access denied. Limited folder review is still available."; model.changed();
                new AlertDialog.Builder(this).setTitle("Continue with limited access").setMessage("You can choose a folder without broad media permission, or grant access in Android Settings.").setPositiveButton("Choose folder",(d,w)->chooseFolder()).setNeutralButton("Settings",(d,w)->{model.waitingSettings=true;startActivity(new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS,Uri.parse("package:"+getPackageName())));}).setNegativeButton("Back",(d,w)->finish()).show();
            }
        }
    }
    @Override public void onActivityResult(int request, int result, Intent data) {
        super.onActivityResult(request, result, data);
        if (request == 30) { launchingTrash = false; model.trashResult(result == RESULT_OK); return; }
        if (request != 10 || result != RESULT_OK || data == null || data.getData() == null) return;
        tree = data.getData();
        int flags = data.getFlags() & (Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
        try {
            if ((flags & Intent.FLAG_GRANT_READ_URI_PERMISSION) != 0) {
                if ((flags & Intent.FLAG_GRANT_WRITE_URI_PERMISSION) != 0) {
                    getContentResolver().takePersistableUriPermission(tree, Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
                } else {
                    getContentResolver().takePersistableUriPermission(tree, Intent.FLAG_GRANT_READ_URI_PERMISSION);
                }
            }
        } catch (SecurityException ignored) { }
        model.folder(tree);
    }
    private void preview(ReviewFile file) {
        if(model.ownCache) {new AlertDialog.Builder(this).setTitle("Clenzy cache").setMessage("This is an old file inside Clenzy’s own disposable cache. Private cache files are not shared with other apps for preview.").setPositiveButton("Done",null).show();return;}
        try { startActivity(new Intent(Intent.ACTION_VIEW).setDataAndType(Uri.parse(file.uri()), getContentResolver().getType(Uri.parse(file.uri()))).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)); }
        catch (ActivityNotFoundException | SecurityException e) { new AlertDialog.Builder(this).setMessage("No preview app is available for this file.").setPositiveButton("Done", null).show(); }
    }
    @Override protected void onSaveInstanceState(Bundle out) { out.putBoolean("trashLaunched", launchingTrash); super.onSaveInstanceState(out); }
    @Override protected void onDestroy() { thumbnails.shutdownNow(); super.onDestroy(); }
    private String bytes(long n) { return n < 0 ? "Unknown size" : Formatter.formatFileSize(this,n); }
    private void refreshMetrics() {
        StatFs disk = new StatFs(Environment.getDataDirectory().getAbsolutePath()); long total = disk.getTotalBytes(), free = disk.getAvailableBytes();
        metrics.setText(bytes(free)+" free of "+bytes(total)); usage.setProgress(total > 0 ? (int)(100.0*(total-free)/total) : 0);
    }
    private LinearLayout column() { LinearLayout v = new LinearLayout(this); v.setOrientation(LinearLayout.VERTICAL); return v; }
    private TextView label(String text, int size, int color) { TextView v = new TextView(this); v.setText(text); v.setTextSize(size); v.setTextColor(color); v.setPadding(0,dp(5),0,dp(5)); return v; }
    private Button button(String title, Runnable action) { Button v = new com.google.android.material.button.MaterialButton(this); v.setText(title); v.setAllCaps(false); v.setTextColor(ink); v.setBackgroundTintList(android.content.res.ColorStateList.valueOf(cardColor)); v.setOnClickListener(w -> action.run()); return v; }
    private void styleCard(View v) { GradientDrawable bg = new GradientDrawable(); bg.setColor(cardColor); bg.setCornerRadius(dp(22)); bg.setStroke(dp(1),Color.argb(40,100,130,190)); v.setBackground(bg); v.setPadding(dp(16),dp(12),dp(16),dp(12)); }
    private LinearLayout.LayoutParams spaced() { LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(-1,-2); p.setMargins(0,dp(14),0,dp(14)); return p; }
    private final class ReviewAdapter extends RecyclerView.Adapter<ReviewAdapter.Holder> {
        List<ReviewFile> entries = List.of();
        void submit(List<ReviewFile> items) { entries = items; notifyDataSetChanged(); }
        class Holder extends RecyclerView.ViewHolder {
            TextView name, detail, group; CheckBox selected; Button preview; ImageView thumbnail;
            Holder(LinearLayout card) { super(card); thumbnail = new ImageView(MainActivity.this); thumbnail.setScaleType(ImageView.ScaleType.CENTER_CROP); card.addView(thumbnail, new LinearLayout.LayoutParams(dp(64),dp(64))); group = label("",11,mint); name=label("",15,ink); detail=label("",12,muted); selected = new CheckBox(MainActivity.this); selected.setText("Select"); selected.setTextColor(mint); preview=button("Preview", () -> {}); card.addView(group); card.addView(name); card.addView(detail); LinearLayout actions = new LinearLayout(MainActivity.this); actions.addView(selected); actions.addView(preview); card.addView(actions); }
        }
        @Override public Holder onCreateViewHolder(ViewGroup parent,int type) { LinearLayout card=column(); styleCard(card); RecyclerView.LayoutParams p=new RecyclerView.LayoutParams(-1,-2); p.setMargins(0,dp(5),0,dp(5)); card.setLayoutParams(p); return new Holder(card); }
        @Override public void onBindViewHolder(Holder h,int position) {
            ReviewFile f=entries.get(position);
            h.thumbnail.setImageResource(android.R.drawable.ic_menu_gallery); h.thumbnail.setTag(f.uri());
            if (Build.VERSION.SDK_INT >= 29) thumbnails.execute(() -> {
                try {
                    android.graphics.Bitmap bitmap = getContentResolver().loadThumbnail(Uri.parse(f.uri()), new android.util.Size(128,128), null);
                    runOnUiThread(() -> { if (!isDestroyed() && f.uri().equals(h.thumbnail.getTag())) h.thumbnail.setImageBitmap(bitmap); });
                } catch (Exception ignored) { }
            });
            h.name.setText(f.name()); h.detail.setText(bytes(f.bytes())+" · "+(f.modified()>0?android.text.format.DateFormat.format("MMM d, yyyy",f.modified()):"Date unavailable")+"\n"+(model.ownCache?"Clenzy cache":model.media?"Authorized photos & videos":"Selected folder"));
            h.group.setVisibility(View.VISIBLE);
            h.group.setText(model.ownCache?"SAFE CLEANUP · CLENZY CACHE":"REVIEW BEFORE DELETING");
            if (model.category == ReviewRules.Category.DUPLICATES) {
                List<ReviewFile> group=ReviewRules.group(model.files,f.digest());
                h.group.setText("MATCHING COPIES · "+group.size()+(group.get(0).uri().equals(f.uri()) ? " · Suggested keep" : ""));
            }
            h.selected.setOnCheckedChangeListener(null); h.selected.setChecked(model.selected.contains(f.uri())); h.selected.setEnabled(!model.busy && model.pendingTrash == null);
            h.selected.setOnCheckedChangeListener((v, checked) -> { if(checked) model.selected.add(f.uri()); else model.selected.remove(f.uri()); updateSelection(); });
            h.preview.setOnClickListener(v -> preview(f));
        }
        @Override public int getItemCount() { return entries.size(); }
    }
}
