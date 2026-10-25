import AppKit
import SwiftUI

struct MainView: View {
    @ObservedObject var model: MainViewModel
    @EnvironmentObject private var settings: SettingsViewModel

    var body: some View {
        ZStack {
            OpsTheme.background.ignoresSafeArea()
            VisualEffectBackground().ignoresSafeArea().opacity(0.55)
            RadialGradient(colors: [OpsTheme.mint.opacity(0.16), .clear], center: .leading, startRadius: 20, endRadius: 520)
                .ignoresSafeArea()
            content
        }
        .foregroundStyle(OpsTheme.text)
        .onAppear { model.onAppear(arguments: CommandLine.arguments) }
        .task {
            while !Task.isCancelled {
                await model.refreshMemory()
                do { try await Task.sleep(nanoseconds: 2_000_000_000) }
                catch { break }
            }
        }
        .onTapGesture { model.holdOpen() }
        .alert("Quit the browser?", isPresented: browserPresented) {
            Button("Quit and clean") { model.answerBrowserQuit(true) }
            Button("Skip the browser", role: .cancel) { model.answerBrowserQuit(false) }
        } message: {
            Text(model.browserPrompt ?? "")
        }
        .sheet(isPresented: $model.showDetails) {
            ScanDetailsView(model: model)
        }
        .sheet(isPresented: $model.showSettings) {
            SettingsPanelView(model: model).environmentObject(settings)
        }
        .sheet(isPresented: $model.showMemoryApps) {
            MemoryAppsView(model: model)
        }
    }

    private var browserPresented: Binding<Bool> {
        Binding(get: { model.browserPrompt != nil }, set: { if !$0 { model.answerBrowserQuit(false) } })
    }

    private var content: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 0) {
            header
            HStack(alignment: .center, spacing: 48) {
                leftColumn
                rightColumn
            }
            .padding(.horizontal, 36)
            .padding(.bottom, 20)
            HomeMetricsView(model: model)
                .padding(.horizontal, 36)
                .padding(.bottom, 12)
            memoryFooter
                .padding(.horizontal, 36)
                .padding(.bottom, 20)
            }
        }
    }

    private var memoryFooter: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 18) {
                Image(systemName: "memorychip").foregroundStyle(OpsTheme.mint)
                VStack(alignment: .leading, spacing: 5) {
                    Text("RAM USAGE · \(Int(model.memory.usageFraction * 100))%")
                        .font(.system(size: 11, weight: .semibold, design: .monospaced))
                    ProgressView(value: model.memory.usageFraction)
                        .tint(OpsTheme.mint)
                        .frame(width: 180)
                }
                VStack(alignment: .leading, spacing: 3) {
                    Text("\(ByteFormat.string(from: Int64(model.memory.usedBytes))) / \(ByteFormat.string(from: Int64(model.memory.totalBytes)))")
                }
                .font(.system(size: 11, design: .monospaced))
                Spacer()
                Button(model.freeingMemory ? "CLEANING RAM…" : "CLEAN UP RAM") { model.freeMemory() }
                    .buttonStyle(.bordered)
                    .tint(OpsTheme.mint)
                    .disabled(model.freeingMemory || model.phase == .working || model.phase == .countingDown)
                    .help("Quits up to five running Dock apps with the lowest measured RAM usage, then opens a review. Finder and Clenzy stay open. Apps may ask you to save.")
            }
            Text("Closes the 5 lowest-RAM Dock apps. Finder + Clenzy stay open. Save prompts are respected.")
                .font(.system(size: 10)).foregroundStyle(OpsTheme.secondary)
            HStack(spacing: 10) {
                Text("TOP APPS")
                    .font(.system(size: 9, weight: .semibold, design: .monospaced))
                    .foregroundStyle(OpsTheme.secondary)
                if model.topMemoryApps.isEmpty {
                    Text("Reading app memory…").font(.caption).foregroundStyle(OpsTheme.secondary)
                } else {
                    ForEach(model.topMemoryApps) { app in
                        HStack(spacing: 5) {
                            Text(app.name).lineLimit(1).truncationMode(.tail)
                            Spacer(minLength: 0)
                            Text(app.percentText).foregroundStyle(OpsTheme.mint).monospacedDigit()
                        }
                        .font(.system(size: 10, weight: .medium))
                        .padding(.horizontal, 8).padding(.vertical, 6)
                        .frame(maxWidth: .infinity)
                        .background(Color.white.opacity(0.05), in: RoundedRectangle(cornerRadius: 6))
                        .help("\(app.name): \(app.percentText) of total RAM. Includes readable helper processes; estimates may differ from Activity Monitor.")
                    }
                }
            }
            .accessibilityElement(children: .contain)
        }
        .modifier(GlassCard())
    }

    private var header: some View {
        HStack(spacing: 12) {
            RoundedRectangle(cornerRadius: 7, style: .continuous)
                .fill(LinearGradient(colors: [OpsTheme.mint, OpsTheme.mintDeep], startPoint: .topLeading, endPoint: .bottomTrailing))
                .frame(width: 22, height: 22)
            Text("CLENZY")
                .font(.system(size: 13, weight: .semibold, design: .monospaced))
                .tracking(3)
            Text("v\(Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "1.0.0")")
                .font(.system(size: 11, design: .monospaced))
                .foregroundStyle(OpsTheme.secondary)
            Spacer()
            Button {
                model.openSettings()
            } label: {
                Image(systemName: "gearshape")
                    .frame(width: 34, height: 34)
                    .background(Color.white.opacity(0.06), in: RoundedRectangle(cornerRadius: 10))
            }
            .buttonStyle(.plain)
            .accessibilityLabel("Settings")
        }
        .padding(.horizontal, 36)
        .padding(.top, 22)
        .padding(.bottom, 8)
    }

    private var leftColumn: some View {
        VStack(spacing: 16) {
            Text("Keep your Mac clean and running smoothly.")
                .font(.system(size: 13))
                .foregroundStyle(OpsTheme.secondary)
                .multilineTextAlignment(.center)
                .frame(maxWidth: 280)
            RadarButton(title: model.phase == .working ? "WAIT" : "CLEAN", spinning: model.phase == .working) {
                model.cleanNow()
            }
            .disabled(model.phase == .working || model.phase == .countingDown || model.freeingMemory)
            Text("Includes Trash · permanently deletes eligible files")
                .font(.system(size: 10)).foregroundStyle(OpsTheme.secondary)
                .multilineTextAlignment(.center)
            if !model.countdownText.isEmpty {
                VStack(spacing: 8) {
                    Text(model.countdownText)
                        .font(.system(size: 13, design: .monospaced))
                    Button("Cancel") { model.cancel() }
                        .buttonStyle(.bordered)
                }
            }
            Text(model.lastCleanedText)
                .font(.system(size: 12, design: .monospaced))
                .foregroundStyle(OpsTheme.secondary)
            if !model.errorMessage.isEmpty {
                Text(model.errorMessage)
                    .font(.system(size: 12))
                    .foregroundStyle(OpsTheme.amber)
            }
        }
        .frame(width: 340)
    }

    private var rightColumn: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("[ TARGETS ]")
                .font(.system(size: 11, weight: .semibold, design: .monospaced))
                .tracking(2)
                .foregroundStyle(OpsTheme.mint)
            ForEach(model.rows) { row in
                VStack(alignment: .leading, spacing: 8) {
                    Button { model.selectTarget(row) } label: {
                        HStack(spacing: 12) {
                            Circle().fill(row.needsAttention ? OpsTheme.amber : OpsTheme.mint)
                                .frame(width: 8, height: 8)
                            VStack(alignment: .leading, spacing: 3) {
                                Text(row.title).font(.system(size: 12, weight: .semibold, design: .monospaced))
                                Text(row.detail).font(.system(size: 12)).foregroundStyle(OpsTheme.secondary).lineLimit(2)
                            }
                            Spacer()
                            Image(systemName: row.needsAttention ? "questionmark.circle" : "chevron.right")
                                .foregroundStyle(row.needsAttention ? OpsTheme.amber : OpsTheme.secondary)
                        }.frame(maxWidth: .infinity, alignment: .leading).contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .accessibilityLabel("\(row.title), \(row.detail)")
                }.modifier(GlassCard())
            }
            HStack {
                Text(model.recoveredText)
                    .font(.system(size: 12, design: .monospaced))
                    .foregroundStyle(OpsTheme.secondary)
                Spacer()
                Button("View details") { Task { await model.preview() } }
                    .buttonStyle(.bordered)
                    .tint(OpsTheme.mint)
            }
            .padding(.top, 6)
            if let result = model.result, model.phase == .completed {
                ResultsView(result: result)
            }

        }
    }
}
