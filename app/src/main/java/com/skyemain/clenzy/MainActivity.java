package com.skyemain.clenzy;

import android.app.*;
import android.content.*;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.*;
import android.text.format.Formatter;
import android.view.*;
import android.widget.*;
import java.util.*;
import java.util.concurrent.*;

public class MainActivity extends Activity {
    private final int mint = Color.rgb(41, 242, 194);
    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private final Set<String> selected = new HashSet<>();
    private List<ReviewFile> files = List.of();
    private DocumentCleaner cleaner;
    private Uri tree;
    private boolean busy;
    private CancellationSignal cancellation;
    private ReviewRules.Category category = ReviewRules.Category.LARGE;
    private TextView status, metrics;
    private LinearLayout rows;
    private Button choose, scan, remove;
    private ProgressBar usage;

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        cleaner = new DocumentCleaner(getContentResolver());
        LinearLayout page = new LinearLayout(this); page.setOrientation(LinearLayout.VERTICAL); page.setPadding(24, 24, 24, 24);
        page.setBackgroundColor(Color.rgb(10, 16, 15));
        page.setOnApplyWindowInsetsListener((v, insets) -> { v.setPadding(24, insets.getSystemWindowInsetTop()+24, 24, insets.getSystemWindowInsetBottom()+24); return insets; });
        TextView title = label("CLENZY", 28, mint); title.setTypeface(null, Typeface.BOLD); page.addView(title);
        page.addView(label("Make room for what matters.", 19, Color.WHITE));
        metrics = label("", 15, Color.WHITE); page.addView(metrics);
        usage = new ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal); usage.setProgressTintList(android.content.res.ColorStateList.valueOf(mint)); page.addView(usage);
        LinearLayout actions = new LinearLayout(this);
        choose = button("Choose folder", () -> {
            Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT_TREE);
            intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_WRITE_URI_PERMISSION | Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION);
            startActivityForResult(intent, 10);
        });
        scan = button("Scan", this::scan);
        actions.addView(choose); actions.addView(scan); page.addView(actions);
        Button cancel = button("Cancel scan", () -> { if (cancellation != null) cancellation.cancel(); }); page.addView(cancel);
        status = label("Choose a folder to review large files, files older than 30 days and exact duplicates. Nothing is selected automatically.", 13, Color.LTGRAY); page.addView(status);
        Spinner picker = new Spinner(this);
        picker.setAdapter(new ArrayAdapter<>(this, android.R.layout.simple_spinner_dropdown_item, new String[]{"Large files · 100 MiB+", "Older than 30 days", "Exact duplicates · up to 64 MiB"}));
        picker.setOnItemSelectedListener(new android.widget.AdapterView.OnItemSelectedListener() {
            public void onItemSelected(AdapterView<?> p, View v, int position, long id) { category = ReviewRules.Category.values()[position]; showRows(); }
            public void onNothingSelected(AdapterView<?> p) {}
        }); page.addView(picker);
        ScrollView scroll = new ScrollView(this); rows = new LinearLayout(this); rows.setOrientation(LinearLayout.VERTICAL); scroll.addView(rows);
        page.addView(scroll, new LinearLayout.LayoutParams(-1, 0, 1));
        remove = button("Delete selected (0)", () -> {
            if (selected.isEmpty()) return;
            new AlertDialog.Builder(this).setTitle("Permanently delete selected files?")
                .setMessage("This may also remove cloud-provider files from synced devices. There may be no recycle bin. File age alone does not mean a file is unnecessary.")
                .setNegativeButton("Cancel", null).setPositiveButton("Delete selected", (dialog, which) -> delete()).show();
        }); page.addView(remove);
        page.addView(label("Android manages other apps’ private caches and RAM. Clenzy only deletes the folder files you select and confirm.", 12, Color.LTGRAY));
        setContentView(page);
        String saved = getPreferences(0).getString("folder", null);
        if (saved != null) tree = Uri.parse(saved);
        refreshMetrics(); showRows();
    }

    private TextView label(String text, int sp, int color) { TextView v = new TextView(this); v.setText(text); v.setTextSize(sp); v.setTextColor(color); v.setPadding(0, 8, 0, 8); return v; }
    private Button button(String text, Runnable action) { Button b = new Button(this); b.setText(text); b.setTextColor(mint); b.setOnClickListener(v -> { if (!busy || text.equals("Cancel scan")) action.run(); }); return b; }
    private String bytes(long n) { return n < 0 ? "Unknown size" : Formatter.formatFileSize(this, n); }
    private void refreshMetrics() {
        StatFs disk = new StatFs(Environment.getDataDirectory().getAbsolutePath());
        ActivityManager.MemoryInfo ram = new ActivityManager.MemoryInfo(); ((ActivityManager)getSystemService(ACTIVITY_SERVICE)).getMemoryInfo(ram);
        metrics.setText(bytes(disk.getAvailableBytes())+" storage available\n"+bytes(ram.availMem)+" RAM available · managed by Android");
        usage.setProgress((int)(100L*(disk.getTotalBytes()-disk.getAvailableBytes())/disk.getTotalBytes()));
    }
    private void setBusy(boolean value) { busy = value; choose.setEnabled(!value); scan.setEnabled(!value); remove.setEnabled(!value && !selected.isEmpty()); }
    private void ui(Runnable action) { runOnUiThread(() -> { if (!isDestroyed()) action.run(); }); }

    @Override public void onActivityResult(int request, int result, Intent data) {
        super.onActivityResult(request, result, data);
        if (request != 10 || result != RESULT_OK || data == null || data.getData() == null) return;
        tree = data.getData();
        int flags = data.getFlags() & (Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
        try { getContentResolver().takePersistableUriPermission(tree, flags); getPreferences(0).edit().putString("folder", tree.toString()).apply(); }
        catch (SecurityException e) { status.setText("Folder access is for this session only."); }
        files = List.of(); selected.clear(); showRows(); scan();
    }
    private void scan() {
        if (tree == null) { status.setText("Choose a folder first. Android restricts some system folders and Downloads roots; select a subfolder."); return; }
        setBusy(true); cancellation = new CancellationSignal();
        status.setText("Scanning chosen folder; up to 5,000 files, 12 levels deep. Duplicate hashing is limited to same-size files up to 64 MiB.");
        worker.execute(() -> {
            try {
                DocumentCleaner.Scan result = cleaner.scan(tree, cancellation);
                ui(() -> { files = result.files(); selected.clear(); status.setText("Reviewed "+files.size()+" files; "+result.skipped()+" unreadable checks."+(result.truncated()?" Scan limit reached; choose a smaller folder.":"")); setBusy(false); showRows(); });
            } catch (Exception e) { ui(() -> { files = List.of(); selected.clear(); status.setText(cancellation.isCanceled()?"Scan cancelled; nothing deleted.":"Folder unavailable. Choose it again to renew access."); setBusy(false); showRows(); }); }
        });
    }
    private void showRows() {
        if (rows == null) return;
        rows.removeAllViews();
        List<ReviewFile> visible = ReviewRules.filter(files, category, System.currentTimeMillis());
        if (visible.isEmpty()) rows.addView(label("No matches in this review.", 16, Color.LTGRAY));
        for (ReviewFile file : visible) {
            CheckBox row = new CheckBox(this); row.setText(file.name()+"\n"+bytes(file.bytes())); row.setTextColor(Color.WHITE); row.setPadding(14, 16, 14, 16);
            row.setChecked(selected.contains(file.uri())); row.setEnabled(!busy);
            GradientDrawable card = new GradientDrawable(); card.setColor(Color.rgb(25, 34, 31)); card.setCornerRadius(22); card.setStroke(1, Color.rgb(32, 82, 65)); row.setBackground(card);
            LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(-1, -2); params.setMargins(0, 0, 0, 12); rows.addView(row, params);
            row.setOnCheckedChangeListener((button, checked) -> { if (checked) selected.add(file.uri()); else selected.remove(file.uri()); updateSelection(); });
            row.setOnLongClickListener(v -> { preview(file); return true; });
        }
        updateSelection();
    }
    private void updateSelection() { if (remove != null) { remove.setText("Delete selected ("+selected.size()+")"); remove.setEnabled(!busy && !selected.isEmpty()); } }
    private void preview(ReviewFile file) {
        try { startActivity(new Intent(Intent.ACTION_VIEW).setDataAndType(Uri.parse(file.uri()), getContentResolver().getType(Uri.parse(file.uri()))).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)); }
        catch (ActivityNotFoundException | SecurityException e) { status.setText("No preview app available for this file."); }
    }
    private void delete() {
        List<ReviewFile> chosen = new ArrayList<>(); for (ReviewFile file : files) if (selected.contains(file.uri())) chosen.add(file);
        setBusy(true); cancellation = new CancellationSignal();
        worker.execute(() -> {
            DocumentCleaner.Outcome result = cleaner.delete(chosen, cancellation);
            ui(() -> { status.setText("Deleted "+result.deleted()+" files ("+bytes(result.bytes())+" measured); "+result.skipped()+" changed, denied or unverifiable files skipped. Scan again to refresh."); files = List.of(); selected.clear(); setBusy(false); showRows(); refreshMetrics(); });
        });
    }
    @Override public void onDestroy() { if (cancellation != null) cancellation.cancel(); worker.shutdownNow(); super.onDestroy(); }
}
