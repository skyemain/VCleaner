package com.skyemain.clenzy;

public record ReviewFile(String uri, String name, long bytes, long modified, String digest) {
    public boolean matches(long currentBytes, long currentModified) {
        return bytes >= 0 && modified > 0 && bytes == currentBytes && modified == currentModified;
    }
}
