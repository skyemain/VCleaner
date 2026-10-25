import Darwin
import Foundation

struct MemorySnapshot: Equatable, Sendable {
    var totalBytes: UInt64
    var usedBytes: UInt64
    var pressure: String
    var swapUsedBytes: UInt64?
    var explanation: String

    var usageFraction: Double {
        totalBytes > 0 ? min(1, Double(usedBytes) / Double(totalBytes)) : 0
    }
}

enum MemoryInfoService {
    static let explanation = "Free RAM asks macOS to release inactive and cached memory. Open apps stay open. macOS may use that memory again when an app needs it."

    static func snapshot() -> MemorySnapshot {
        let total = ProcessInfo.processInfo.physicalMemory
        var used: UInt64 = 0
        var stats = vm_statistics64()
        var count = mach_msg_type_number_t(MemoryLayout<vm_statistics64_data_t>.stride / MemoryLayout<integer_t>.stride)
        let result = withUnsafeMutablePointer(to: &stats) { pointer -> kern_return_t in
            pointer.withMemoryRebound(to: integer_t.self, capacity: Int(count)) {
                host_statistics64(mach_host_self(), HOST_VM_INFO64, $0, &count)
            }
        }
        if result == KERN_SUCCESS {
            var pageSize: vm_size_t = 0
            host_page_size(mach_host_self(), &pageSize)
            let page = UInt64(pageSize)
            // Exclude reclaimable file-backed and purgeable pages; include compressed memory.
            let resident = Int64(stats.active_count) + Int64(stats.inactive_count) + Int64(stats.wire_count) + Int64(stats.compressor_page_count)
            let reclaimable = Int64(stats.external_page_count) + Int64(stats.purgeable_count)
            used = min(total, UInt64(max(0, resident - reclaimable)) * page)
        }
        return MemorySnapshot(
            totalBytes: total,
            usedBytes: used,
            pressure: pressureLabel(),
            swapUsedBytes: swapUsed(),
            explanation: explanation
        )
    }

    static func pressureLevel() -> Int32 {
        var level = Int32(0)
        var size = MemoryLayout<Int32>.size
        guard sysctlbyname("kern.memorystatus_vm_pressure_level", &level, &size, nil, 0) == 0 else {
            return 0
        }
        return level
    }

    private static func pressureLabel() -> String {
        switch pressureLevel() {
        case 1: return "Normal"
        case 2: return "Warning"
        case 4: return "Urgent"
        case 8: return "Critical"
        default: return "Normal"
        }
    }

    private static func swapUsed() -> UInt64? {
        var usage = xsw_usage()
        var size = MemoryLayout<xsw_usage>.size
        guard sysctlbyname("vm.swapusage", &usage, &size, nil, 0) == 0 else { return nil }
        return usage.xsu_used
    }
}

struct MemoryPurgeOutcome: Equatable, Sendable {
    var freedBytes: Int64
    var message: String
}

enum MemoryPurgeService {
    /// Inactive plus purgeable pages, never more than 1 GB and never more than half of physical memory.
    /// Below 128 MB there is nothing useful to release.
    static func budget(inactive: UInt64, purgeable: UInt64, physical: UInt64) -> UInt64 {
        let candidate = inactive &+ purgeable
        let half = physical / 2
        let oneGigabyte: UInt64 = 1024 * 1024 * 1024
        let cap = min(candidate, half, oneGigabyte)
        if cap < 128 * 1024 * 1024 { return 0 }
        return cap
    }

    static func purge() -> MemoryPurgeOutcome {
        let before = memorySample()
        let cap = budget(inactive: before.inactive, purgeable: before.purgeable, physical: before.physical)
        guard cap > 0 else {
            return MemoryPurgeOutcome(freedBytes: 0, message: "Memory is already lean.")
        }
        reclaim(bytes: cap)
        Thread.sleep(forTimeInterval: 0.4)
        let after = memorySample()
        let freed = Int64(after.free) - Int64(before.free)
        if freed >= 8 * 1024 * 1024 {
            return MemoryPurgeOutcome(freedBytes: freed, message: "Freed \(ByteFormat.string(from: freed)).")
        }
        return MemoryPurgeOutcome(freedBytes: 0, message: "macOS is already using this memory for open apps.")
    }

    private struct Sample {
        var free: UInt64
        var inactive: UInt64
        var purgeable: UInt64
        var physical: UInt64
    }

    private static func memorySample() -> Sample {
        let physical = ProcessInfo.processInfo.physicalMemory
        var stats = vm_statistics64()
        var count = mach_msg_type_number_t(MemoryLayout<vm_statistics64_data_t>.stride / MemoryLayout<integer_t>.stride)
        let result = withUnsafeMutablePointer(to: &stats) { pointer -> kern_return_t in
            pointer.withMemoryRebound(to: integer_t.self, capacity: Int(count)) {
                host_statistics64(mach_host_self(), HOST_VM_INFO64, $0, &count)
            }
        }
        guard result == KERN_SUCCESS else {
            return Sample(free: 0, inactive: 0, purgeable: 0, physical: physical)
        }
        var pageSize: vm_size_t = 0
        host_page_size(mach_host_self(), &pageSize)
        let page = UInt64(pageSize)
        return Sample(
            free: UInt64(stats.free_count) * page,
            inactive: UInt64(stats.inactive_count) * page,
            purgeable: UInt64(stats.purgeable_count) * page,
            physical: physical
        )
    }

    /// Touch inactive pages so the kernel reclaims them, then release the allocation. Stops if memory pressure rises.
    private static func reclaim(bytes: UInt64) {
        let chunk = 16 * 1024 * 1024
        var held: [UnsafeMutableRawPointer] = []
        var remaining = bytes
        while remaining > 0 {
            if MemoryInfoService.pressureLevel() >= 2 { break }
            let size = Int(min(UInt64(chunk), remaining))
            guard let pointer = malloc(size) else { break }
            memset(pointer, 1, size)
            held.append(pointer)
            remaining -= UInt64(size)
        }
        for pointer in held {
            free(pointer)
        }
    }
}
