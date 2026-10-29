package com.skyemain.clenzy;

import java.util.*;

public final class ReviewRules {
    public enum Category { LARGE, OLD, DUPLICATES, SCREENSHOTS, VIDEOS, ALL }
    public static List<ReviewFile> filter(List<ReviewFile> files, Category category, long now) {
        Map<String, Integer> hashes = new HashMap<>();
        for (ReviewFile file : files) if (file.digest() != null) hashes.merge(file.digest(), 1, Integer::sum);
        List<ReviewFile> result = new ArrayList<>();
        for (ReviewFile file : files) {
            boolean include = switch (category) {
                case ALL -> true;
                case SCREENSHOTS -> file.name() != null && file.name().toLowerCase(Locale.ROOT).contains("screenshot");
                case VIDEOS -> file.uri().contains("/video/") || file.name().toLowerCase(Locale.ROOT).matches(".*\\.(mp4|mov|mkv|webm|avi|3gp)$");
                case LARGE -> file.bytes() >= 100L * 1024 * 1024;
                case OLD -> file.modified() > 0 && file.modified() < now - 30L * 24 * 60 * 60 * 1000;
                case DUPLICATES -> file.digest() != null && hashes.getOrDefault(file.digest(), 0) > 1;
            };
            if (include) result.add(file);
        }
        result.sort(Comparator.comparingLong(ReviewFile::bytes).reversed().thenComparing(ReviewFile::uri));
        if (category == Category.DUPLICATES) result.sort(Comparator.comparing(ReviewFile::digest).thenComparingLong(ReviewFile::modified).thenComparing(ReviewFile::uri));
        return result;
    }
    public static long selectedBytes(List<ReviewFile> files,Set<String> selected) {
        Set<String> seen=new HashSet<>();long total=0;
        for(ReviewFile f:files)if(selected.contains(f.uri()) && seen.add(f.uri()) && f.bytes()>0)total+=f.bytes();
        return total;
    }
    public static List<ReviewFile> group(List<ReviewFile> files, String digest) {
        List<ReviewFile> group = new ArrayList<>();
        for (ReviewFile f : files) if (digest != null && digest.equals(f.digest())) group.add(f);
        group.sort(Comparator.comparingLong(ReviewFile::modified).thenComparing(ReviewFile::uri));
        return group;
    }
    public static boolean selectsEveryCopy(List<ReviewFile> files, Set<String> selected) {
        Set<String> checked = new HashSet<>();
        for (ReviewFile f : files) if (f.digest() != null && checked.add(f.digest())) {
            List<ReviewFile> group = group(files, f.digest());
            if (group.size() > 1 && group.stream().allMatch(item -> selected.contains(item.uri()))) return true;
        }
        return false;
    }
}
