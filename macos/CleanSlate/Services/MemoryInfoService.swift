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
    static let explanation = "macOS manages cached memory automatically. To release memory held by an app, review the largest apps and choose one to quit normally. No app is closed without your confirmation."

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
        default: return "Unavailable"
        }
    }

    private static func swapUsed() -> UInt64? {
        var usage = xsw_usage()
        var size = MemoryLayout<xsw_usage>.size
        guard sysctlbyname("vm.swapusage", &usage, &size, nil, 0) == 0 else { return nil }
        return usage.xsu_used
    }
}
