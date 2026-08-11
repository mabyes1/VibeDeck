import { getIntlLocale, tLegacy } from "./i18n.js?v=4";

export function createSideboardController({
  elements,
  fetchJsonOrThrow,
  isTrustRequiredError,
  getActiveMode,
  setText,
  setBar,
  formatters,
  onConnectionChange,
  onWorkPulse,
}) {
  const {
    sideHeadline,
    sideSummary,
    sideError,
    sideLoad,
    sideLoadNormal,
    sideLoadStatus,
    sideLoadStatusReason,
    sideLoadAlert,
    sideLoadAlertTitle,
    sideLoadAlertReason,
    sideHost,
    sideUptime,
    sideHealth,
    sideCpu,
    sideCpuSub,
    sideCpuBar,
    sideRam,
    sideRamSub,
    sideRamBar,
    sideGpu,
    sideGpuSub,
    sideGpuBar,
    sideVram,
    sideVramSub,
    sideVramBar,
    sideNet,
    sideNetSub,
    sideNetBar,
    sideDisk,
    sideDiskSub,
    sideDiskBar,
    sideDiskIo,
    sideWeather,
    sideWeatherSub,
    sideProcessList,
  } = elements;
  const {
    formatPercent,
    formatGb,
    formatMbps,
    formatTemperature,
    describeWeatherCode,
    formatWeatherLocation,
    formatSeconds,
    averagePercent,
  } = formatters;

  const warningThreshold = 90;
  function renderLoadState(stats) {
    const cpu = Number(stats?.cpu?.usagePercent);
    const memory = Number(stats?.memory?.usagePercent);
    const gpu = Number(stats?.gpu?.usagePercent);
    const candidates = [
      Number.isFinite(cpu) ? { label: "CPU", value: cpu } : null,
      Number.isFinite(memory) ? { label: tLegacy("記憶體"), value: memory } : null,
      Number.isFinite(gpu) && gpu > 0 ? { label: "GPU", value: gpu } : null,
    ].filter(Boolean);
    const maxValue = candidates.length ? Math.max(...candidates.map(item => item.value)) : 0;
    const status = maxValue >= 97 ? tLegacy("系統狀態極重度") : maxValue >= warningThreshold ? tLegacy("系統狀態重度") : maxValue >= 75 ? tLegacy("系統狀態中度") : maxValue >= 60 ? tLegacy("系統狀態輕度") : tLegacy("系統狀態良好");
    const highest = candidates.filter(item => item.value >= 60).sort((a, b) => b.value - a.value);
    sideLoadNormal.hidden = false;
    sideLoadAlert.hidden = true;
    sideLoadStatus.textContent = status;
    sideLoadStatusReason.textContent = highest.length
      ? highest.map(item => `${item.label} ${Math.round(item.value)}%`).join(" · ")
      : tLegacy("目前沒有明顯瓶頸");
  }

  function renderProcesses(processes) {
    sideProcessList.replaceChildren();
    for (const process of processes.slice(0, 4)) {
      const li = document.createElement("li");
      const name = document.createElement("span");
      const value = document.createElement("b");
      name.textContent = process.name || process.Name || "process";
      value.textContent = process.memoryMb || process.MemoryMb
        ? `${Math.round(process.memoryMb || process.MemoryMb)}MB`
        : "";
      li.append(name, value);
      sideProcessList.append(li);
    }

    if (!sideProcessList.children.length) {
      const li = document.createElement("li");
      li.textContent = tLegacy("沒有程序資料");
      sideProcessList.append(li);
    }
  }

  function renderStats(stats) {
    const cpu = stats.cpu || {};
    const memory = stats.memory || {};
    const gpu = stats.gpu || {};
    const network = stats.network || {};
    const disk = stats.disk || {};
    const weather = stats.weather || {};
    const system = stats.system || {};
    const load = averagePercent([
      cpu.usagePercent,
      memory.usagePercent,
      gpu.usagePercent,
      disk.usagePercent,
    ]);

    sideHeadline.textContent = system.hostname || tLegacy("VibeDeck 資訊板");
    sideSummary.textContent = `${new Date(stats.generatedAt || Date.now()).toLocaleTimeString(getIntlLocale(), { hour: "2-digit", minute: "2-digit" })}`;
    sideLoad.textContent = formatPercent(load);
    renderLoadState(stats);
    sideHost.textContent = `${tLegacy("主機")} ${system.localIp || "--"}`;
    sideUptime.textContent = `${tLegacy("已運行")} ${formatSeconds(system.uptimeSeconds)}`;
    sideHealth.textContent = stats.error ? `${tLegacy("收集器：")}${stats.error}` : tLegacy("收集器正常");

    setText(sideCpu, formatPercent(cpu.usagePercent));
    setText(sideCpuSub, `${tLegacy("溫度")} ${formatTemperature(cpu.temperatureC)}`);
    setBar(sideCpuBar, cpu.usagePercent);

    setText(sideRam, formatPercent(memory.usagePercent));
    setText(sideRamSub, `${formatGb(memory.usedGb)} / ${formatGb(memory.totalGb)}`);
    setBar(sideRamBar, memory.usagePercent);

    setText(sideGpu, formatPercent(gpu.usagePercent));
    setText(sideGpuSub, [gpu.name, formatTemperature(gpu.temperatureC)].filter(Boolean).join(" · "));
    setBar(sideGpuBar, gpu.usagePercent);

    setText(sideVram, formatPercent(gpu.memoryUsagePercent));
    setText(sideVramSub, `${Math.round(gpu.memoryUsedMb || 0)} / ${Math.round(gpu.memoryTotalMb || 0)} MB`);
    setBar(sideVramBar, gpu.memoryUsagePercent);

    setText(sideNet, `${formatMbps(network.downMbps)}↓`);
    setText(sideNetSub, `${formatMbps(network.upMbps)} Mbps ${tLegacy("上傳")}`);
    setBar(sideNetBar, Math.min(100, Math.max(network.downMbps || 0, network.upMbps || 0) * 5));

    setText(sideDisk, formatPercent(disk.usagePercent));
    setText(sideDiskSub, `${disk.drive || tLegacy("磁碟")} · ${formatGb(disk.usedGb)} / ${formatGb(disk.totalGb)}`);
    setBar(sideDiskBar, disk.usagePercent);

    const weatherTemp = Number.isFinite(weather.temperatureC) ? formatTemperature(weather.temperatureC) : "";
    const weatherFeels = Number.isFinite(weather.apparentTemperatureC) ? formatTemperature(weather.apparentTemperatureC) : "--";
    const weatherDescription = describeWeatherCode(weather.weatherCode ?? weather.WeatherCode, weather.description || weather.Description);
    const weatherParts = [
      formatWeatherLocation(weather.location || weather.Location),
      weatherDescription,
      weatherTemp,
    ].filter(Boolean);
    setText(sideWeather, weatherParts.length > 1 ? weatherParts.join(" · ") : tLegacy("天氣資料暫不可用"));
    setText(sideWeatherSub, `${tLegacy("體感")} ${weatherFeels}`);
    setText(sideDiskIo, `${tLegacy("磁碟 IO")} ${tLegacy("讀")} ${formatMbps(disk.readMBps)} / ${tLegacy("寫")} ${formatMbps(disk.writeMBps)} MB/s`);
    renderProcesses(stats.processes || []);
  }

  async function refresh() {
    if (getActiveMode() !== "sideboard") return;

    try {
      const [stats, workPulse] = await Promise.all([
        fetchJsonOrThrow("/api/sideboard/stats"),
        fetchJsonOrThrow("/api/sideboard/work-pulse").catch(() => null),
      ]);

      sideError.textContent = "";
      renderStats(stats);
      onWorkPulse?.(workPulse);
      onConnectionChange?.("online");
    } catch (error) {
      onConnectionChange?.("connecting");
      if (isTrustRequiredError(error)) {
        sideHeadline.textContent = tLegacy("資訊板已鎖定");
        sideSummary.textContent = tLegacy("請先配對手機。");
        sideError.textContent = tLegacy("需要信任裝置。");
        sideError.removeAttribute("title");
        onWorkPulse?.(null);
        return;
      }

      sideHeadline.textContent = tLegacy("資訊板暫時無法更新");
      sideSummary.textContent = tLegacy("目前保留最近一次資料；可按「重新連線」再試。");
      sideError.textContent = tLegacy("本機資訊讀取暫時中斷");
      sideError.title = error.message || tLegacy("資料收集器無法使用。");
      onWorkPulse?.(null);
    }
  }

  return { refresh };
}
