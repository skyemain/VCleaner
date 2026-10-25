package com.skyemain.clenzy;

import java.util.*;

public final class ReviewRules {
    public enum Category { LARGE, OLD, DUPLICATES }
    public static List<ReviewFile> filter(List<ReviewFile> files, Category category, long now) {
        Map<String, Integer> hashes = new HashMap<>();
        for (ReviewFile file : files) if (file.digest() != null) hashes.merge(file.digest(), 1, Integer::sum);
        List<ReviewFile> result = new ArrayList<>();
        for (ReviewFile file : files) {
            boolean include = switch (category) {
                case LARGE -> file.bytes() >= 100L * 1024 * 1024;
                case OLD -> file.modified() > 0 && file.modified() < now - 30L * 24 * 60 * 60 * 1000;
                case DUPLICATES -> file.digest() != null && hashes.getOrDefault(file.digest(), 0) > 1;
            };
            if (include) result.add(file);
        }
        result.sort(Comparator.comparingLong(ReviewFile::bytes).reversed().thenComparing(ReviewFile::uri));
        return result;
    }
}
