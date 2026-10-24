import Darwin
import Foundation

struct MemorySnapshot: Equatable, Sendable {
    var totalBytes: UInt64
    var usedBytes: UInt64
    var pressure: String
    var swapUsedBytes: UInt64?
    var explanation: String
}

enum MemoryInfoService {
    static let explanation = "macOS manages memory automatically. CleanSlate does not purge caches or quit apps to raise the free-memory number."

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
            used = (UInt64(stats.active_count) + UInt64(stats.wire_count) + UInt64(stats.compressor_page_count)) * page
        }
        return MemorySnapshot(
            totalBytes: total,
            usedBytes: used,
            pressure: pressureLabel(),
            swapUsedBytes: swapUsed(),
            explanation: explanation
        )
    }

    private static func pressureLabel() -> String {
        var level = Int32(0)
        var size = MemoryLayout<Int32>.size
        guard sysctlbyname("kern.memorystatus_vm_pressure_level", &level, &size, nil, 0) == 0 else {
            return "Unavailable"
        }
        switch level {
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
