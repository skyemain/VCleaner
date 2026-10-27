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

public class MainActivity extends ComponentActivity {
    private final int mint = Color.rgb(68, 239, 196), muted = Color.rgb(151, 179, 173);
    private CleanupViewModel model;
    private Uri tree;
    private TextView status, metrics, selection;
    private Button choose, photos, scan, deep, cancel, remove;
    private ProgressBar usage, progress;
    private ReviewAdapter adapter;
    private boolean launchingTrash;
    private boolean sortByName;
    private final java.util.concurrent.ExecutorService thumbnails = java.util.concurrent.Executors.newFixedThreadPool(2);
    private int dp(float value) { return Math.round(value * getResources().getDisplayMetrics().density); }

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        model = new ViewModelProvider(this).get(CleanupViewModel.class); tree = model.tree;
        LinearLayout page = column(); page.setPadding(dp(22), dp(20), dp(22), dp(16));
        page.setBackground(new GradientDrawable(GradientDrawable.Orientation.TL_BR, new int[]{Color.rgb(15,42,36),Color.rgb(9,15,20)}));
        page.setOnApplyWindowInsetsListener((v, insets) -> { v.setPadding(dp(22), insets.getSystemWindowInsetTop()+dp(12), dp(22), insets.getSystemWindowInsetBottom()+dp(12)); return insets; });
        TextView brand = label("✦  C L E N Z Y", 14, mint); brand.setTypeface(null, Typeface.BOLD); page.addView(brand);
        TextView title = label("A little lighter.", 30, Color.WHITE); title.setTypeface(null, Typeface.BOLD); page.addView(title);
        page.addView(label("Keep what matters. Make room for more.", 14, muted));
        LinearLayout storage = column(); styleCard(storage);
        storage.addView(label("DEVICE STORAGE", 11, mint)); metrics = label("", 22, Color.WHITE); storage.addView(metrics);
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
        picker.setAdapter(new ArrayAdapter<>(this, android.R.layout.simple_spinner_dropdown_item, new String[]{"Large files · 100 MiB+", "Older than 30 days", "Exact duplicates", "Screenshots (name match)", "Videos"}));
        picker.setSelection(model.category.ordinal());
        picker.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
            public void onItemSelected(AdapterView<?> p, View v, int position, long id) { model.category = ReviewRules.Category.values()[position]; model.changed(); }
            public void onNothingSelected(AdapterView<?> p) {}
        });
        LinearLayout filters = new LinearLayout(this); filters.addView(picker, new LinearLayout.LayoutParams(0,-2,1));
        Button sort = button("Sort", () -> new AlertDialog.Builder(this).setTitle("Sort files").setItems(new String[]{"Largest first", "Name"}, (d,which) -> { sortByName = which == 1; render(); }).show()); filters.addView(sort); page.addView(filters);
        RecyclerView list = new RecyclerView(this); list.setLayoutManager(new LinearLayoutManager(this)); adapter = new ReviewAdapter(); list.setAdapter(adapter);
        page.addView(list, new LinearLayout.LayoutParams(-1,0,1));
        selection = label("", 12, muted); page.addView(selection);
        remove = button("Review selected", this::confirmDelete); page.addView(remove);
        TextView help = label("Private by design · only files you authorize", 11, muted); help.setGravity(Gravity.CENTER); help.setOnClickListener(v -> new AlertDialog.Builder(this).setTitle("Your files, your choice").setMessage("Clenzy cannot clear other apps' private caches or RAM. Photos use Android's Trash confirmation on Android 11+. Folder deletion may be permanent and may affect cloud copies. Age alone never means junk.").setPositiveButton("Done", null).setNeutralButton("Permissions", (d,w) -> startActivity(new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:"+getPackageName())))).show()); page.addView(help);
        launchingTrash = state != null && state.getBoolean("trashLaunched", false);
        setContentView(page);
        model.updates.observe(this, ignored -> render());
    }
    private void render() {
        tree = model.tree; status.setText(model.status); progress.setVisibility(model.busy ? View.VISIBLE : View.GONE); cancel.setVisibility(model.busy ? View.VISIBLE : View.GONE);
        for (Button b : List.of(choose, photos, scan, deep)) b.setEnabled(!model.busy && model.pendingTrash == null);
        List<ReviewFile> visible = ReviewRules.filter(model.files, model.category, System.currentTimeMillis());
        if (sortByName && model.category != ReviewRules.Category.DUPLICATES) visible.sort(Comparator.comparing(ReviewFile::name, String.CASE_INSENSITIVE_ORDER));
        adapter.submit(visible);
        updateSelection(); refreshMetrics();
        if (model.pendingTrash != null && !launchingTrash) {
            launchingTrash = true;
            try { startIntentSenderForResult(model.pendingTrash.getIntentSender(), 30, null, 0,0,0); }
            catch (IntentSender.SendIntentException e) { launchingTrash = false; model.trashResult(false); }
        }
    }
    private void updateSelection() {
        long bytes = 0; for (ReviewFile f : model.chosen()) if (f.bytes() >= 0) bytes += f.bytes();
        selection.setText(model.selected.isEmpty() ? "Nothing selected · tap Preview to inspect a file" : model.selected.size()+" selected · "+bytes(bytes)+" source size");
        remove.setText(model.media ? "Review & move to Trash" : "Review & delete files"); remove.setEnabled(!model.busy && model.pendingTrash == null && !model.selected.isEmpty());
    }
    private void confirmDelete() {
        boolean all = ReviewRules.selectsEveryCopy(model.files, model.selected);
        new AlertDialog.Builder(this).setTitle(model.media ? "Move selected media to Trash?" : "Permanently delete selected files?")
            .setMessage((all ? "You selected EVERY copy in a duplicate group.\n\n" : "") + (model.media ? "Android will ask you to confirm. Trashed items may still use storage until removed." : "There may be no recycle bin. Cloud providers may delete synced copies too. Age does not mean a file is unnecessary."))
            .setNegativeButton("Cancel", null).setPositiveButton("Continue", (d,w) -> model.delete()).show();
    }
    private void chooseFolder() {
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT_TREE);
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_WRITE_URI_PERMISSION | Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION);
        startActivityForResult(intent, 10);
    }
    private void mediaAccess() {
        ArrayList<String> permissions = new ArrayList<>();
        if (Build.VERSION.SDK_INT >= 33) { permissions.add(Manifest.permission.READ_MEDIA_IMAGES); permissions.add(Manifest.permission.READ_MEDIA_VIDEO); if (Build.VERSION.SDK_INT >= 34) permissions.add(Manifest.permission.READ_MEDIA_VISUAL_USER_SELECTED); }
        else permissions.add(Manifest.permission.READ_EXTERNAL_STORAGE);
        boolean granted = true; for (String p : permissions) if (checkSelfPermission(p) != PackageManager.PERMISSION_GRANTED) granted = false;
        if (!granted) requestPermissions(permissions.toArray(new String[0]), 20); else { model.media = true; model.scan(false); }
    }
    @Override public void onRequestPermissionsResult(int request, String[] permissions, int[] grants) {
        super.onRequestPermissionsResult(request, permissions, grants);
        if (request == 20) {
            boolean any = false; for (int g : grants) if (g == PackageManager.PERMISSION_GRANTED) any = true;
            if (any) { model.media = true; model.scan(false); }
            else { model.status = "Photos access denied. Choose a folder instead or change access in app settings."; model.changed(); }
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
    private Button button(String title, Runnable action) { Button v = new Button(this); v.setText(title); v.setAllCaps(false); v.setTextColor(mint); v.setOnClickListener(w -> action.run()); return v; }
    private void styleCard(View v) { GradientDrawable bg = new GradientDrawable(); bg.setColor(Color.argb(150,30,54,48)); bg.setCornerRadius(dp(22)); bg.setStroke(dp(1),Color.rgb(42,79,69)); v.setBackground(bg); v.setPadding(dp(16),dp(12),dp(16),dp(12)); }
    private LinearLayout.LayoutParams spaced() { LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(-1,-2); p.setMargins(0,dp(14),0,dp(14)); return p; }
    private final class ReviewAdapter extends RecyclerView.Adapter<ReviewAdapter.Holder> {
        List<ReviewFile> entries = List.of();
        void submit(List<ReviewFile> items) { entries = items; notifyDataSetChanged(); }
        class Holder extends RecyclerView.ViewHolder {
            TextView name, detail, group; CheckBox selected; Button preview; ImageView thumbnail;
            Holder(LinearLayout card) { super(card); thumbnail = new ImageView(MainActivity.this); thumbnail.setScaleType(ImageView.ScaleType.CENTER_CROP); card.addView(thumbnail, new LinearLayout.LayoutParams(dp(64),dp(64))); group = label("",11,mint); name=label("",15,Color.WHITE); detail=label("",12,muted); selected = new CheckBox(MainActivity.this); selected.setText("Select"); selected.setTextColor(mint); preview=button("Preview", () -> {}); card.addView(group); card.addView(name); card.addView(detail); LinearLayout actions = new LinearLayout(MainActivity.this); actions.addView(selected); actions.addView(preview); card.addView(actions); }
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
            h.name.setText(f.name()); h.detail.setText(bytes(f.bytes()));
            h.group.setVisibility(model.category == ReviewRules.Category.DUPLICATES ? View.VISIBLE : View.GONE);
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
