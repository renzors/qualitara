// Design A · Queue — renders the triage queue and the selected account from window.RELAY.
(function () {
  const D = window.RELAY;
  const selected = +document.body.dataset.selected;
  const LABEL = { unusual: 'Unusual', look: 'Worth a look', normal: 'Normal', insufficient: 'Not enough history', none: 'No activity on record' };
  const NICE = { 'Total events': 'All activity', 'Calls': 'Calls', 'Leads': 'Leads', 'Appointments': 'Appointments', 'Missed-call rate': 'Missed-call rate', 'Lead conversion': 'Lead conversion', 'No-show rate': 'No-show rate', 'Median talk time': 'Median talk time', 'Off-hours share': 'Activity at unusual hours' };
  const polWord = p => p > 0 ? 'better than usual' : p < 0 ? 'worse than usual' : 'different from usual';
  const esc = s => String(s).replace(/[&<>]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c]));
  const order = ['unusual', 'look', 'normal', 'insufficient', 'none'];

  // queue
  const q = document.getElementById('queue');
  const groups = { unusual: [], look: [], normal: [], other: [] };
  D.accounts.forEach(a => (groups[a.verdict] || groups.other).push(a));
  let html = '';
  [['unusual', 'Unusual'], ['look', 'Worth a look'], ['normal', 'Normal'], ['other', "Can't assess"]].forEach(([k, t]) => {
    const list = groups[k];
    if (!list.length) return;
    html += `<div class="group"><span>${t}</span><span class="num">${list.length}</span></div>`;
    list.forEach(a => {
      const why = a.reasons[0] ? a.reasons[0].text.replace('Off-hours share', 'Unusual-hours activity') : a.verdict === 'none' ? 'No events since the account was created' : 'Within the usual range on every signal';
      html += `<button class="item" data-v="${a.verdict}" aria-current="${a.id === selected}" onclick="location.href='${a.id === 6 ? 'account.html' : 'index.html'}'">
        <span class="dot"></span><span class="name">${esc(a.name)}</span><span class="vol num">${a.recent} <span class="dim">/ ~${Math.round(a.expected)}</span></span>
        <span class="why">${esc(why)}</span>
        <span class="meta">${esc(a.industry)} · ${a.locations} location${a.locations === 1 ? '' : 's'}</span></button>`;
    });
  });
  q.insertAdjacentHTML('beforeend', html);

  // detail
  const a = D.accounts.find(x => x.id === selected);
  const x = selected === 6 ? D.metro : D.oldtown;
  const pane = document.getElementById('detail');
  const local = (iso, tz) => new Date(iso).toLocaleString('en-US', { timeZone: tz, month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });
  const recentEnd = '2026-07-27T22:20:34Z', recentStart = '2026-07-20T22:20:34Z';

  const reasonsHtml = a.reasons.map(r => `<li>${esc(r.text.replace('Off-hours share', 'Share of activity at unusual hours'))} <span class="dim">(${polWord(r.pol)})</span></li>`).join('');
  const sentence = selected === 6
    ? 'Lead conversion dropped to 1 of 15 leads this week. Two sites (J and M) look unusual on their own, but each has 8 or fewer events, so treat them as leads to check rather than conclusions.'
    : '4 of this week’s 9 events happened between 5pm and 4am local time, when this shop usually has almost no activity. With only 9 events, one busy evening can cause this.';

  // metrics table
  const rows = a.metrics.map(m => {
    if (m.na) return `<tr data-v="na"><td>${NICE[m.name]}</td><td class="r dim">—</td><td class="dim">Not assessed: ${m.n ?? 0} events, needs 10</td><td></td><td><span class="pill">Not assessed</span></td></tr>`;
    const lo = parseFloat(m.lo), hi = parseFloat(m.hi), v = parseFloat(m.value);
    const max = Math.max(hi, v) * 1.15 || 1;
    const pct = n => Math.min(100, Math.max(0, (n / max) * 100));
    const st = m.status;
    return `<tr data-v="${st}"><td>${NICE[m.name]}</td><td class="r num">${m.value}</td><td class="num dim">${m.lo} – ${m.hi}</td>
      <td><div class="range"><div class="band" style="left:${pct(lo)}%;width:${pct(hi) - pct(lo)}%"></div><div class="mark" style="left:calc(${pct(v)}% - 1px)"></div></div></td>
      <td><span class="pill">${LABEL[st]}${st !== 'normal' ? ' · ' + (m.pol > 0 ? 'good' : m.pol < 0 ? 'bad' : 'neutral') : ''}</span></td></tr>`;
  }).join('');

  // heatmap
  const dayIdx = [1, 2, 3, 4, 5, 6, 0], dayName = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
  const tot = x.baseHours.reduce((s, n) => s + n, 0);
  const rare = x.baseHours.map(n => n / tot < 0.02);
  const hmax = Math.max(...x.heat.flat(), 1);
  let heat = '';
  dayIdx.forEach(d => {
    heat += `<span class="lab">${dayName[d]}</span>`;
    x.heat[d].forEach((n, h) => {
      const bg = n ? `background:color-mix(in srgb, var(--accent) ${25 + 75 * n / hmax}%, var(--heat-0))` : '';
      heat += `<span class="c${rare[h] ? ' rare' : ''}" style="${bg}" title="${dayName[d]} ${h}:00 — ${n} events"></span>`;
    });
  });
  const bmax = Math.max(...x.baseHours);
  const hours = '<span class="lab" style="font-size:10px;color:var(--muted)">usual</span>' + x.baseHours.map((n, h) => `<i class="${rare[h] ? 'rare' : ''}" style="height:${Math.max(1, 44 * n / bmax)}px" title="${h}:00 — ${Math.round(100 * n / tot)}% of usual activity"></i>`).join('');

  // trend (metro: conversion; oldtown: weekly volume)
  const series = selected === 6 ? D.metro.conversion : a.weeks;
  const W = 600, H = 120, P = 24;
  const smax = Math.max(...series) * 1.1;
  const sx = i => P + i * (W - 2 * P) / (series.length - 1), sy = v => H - 18 - (v / smax) * (H - 30);
  const pts = series.map((v, i) => `${sx(i).toFixed(1)},${sy(v).toFixed(1)}`).join(' ');
  const bm = a.metrics.find(m => m.name === (selected === 6 ? 'Lead conversion' : 'Total events'));
  const bandLo = parseFloat(bm.lo), bandHi = parseFloat(bm.hi);
  const spark = `<svg class="spark" viewBox="0 0 ${W} ${H}" role="img" aria-label="Weekly trend">
    <rect x="${sx(series.length - 9)}" y="${sy(bandHi)}" width="${sx(series.length - 2) - sx(series.length - 9)}" height="${sy(bandLo) - sy(bandHi)}" style="fill:var(--accent-soft)"/>
    <rect x="${sx(series.length - 1) - 8}" y="6" width="16" height="${H - 24}" style="fill:var(--cs)" data-v="${a.verdict}"/>
    <polyline points="${pts}" style="fill:none;stroke:var(--accent)" stroke-width="1.6"/>
    <circle cx="${sx(series.length - 1)}" cy="${sy(series.at(-1))}" r="4" style="fill:var(--c)" data-v="${a.verdict}"/>
    <text x="${P}" y="${H - 4}">Feb 9</text><text x="${W / 2}" y="${H - 4}" text-anchor="middle">May 4</text><text x="${W - P}" y="${H - 4}" text-anchor="end">Jul 27</text></svg>`;

  const sites = selected === 6 ? D.metro.locations.slice().sort((p, q2) => order.indexOf(p.v) - order.indexOf(q2.v)).map(s =>
    `<div class="site" data-v="${s.v}"><b>${s.site}</b><small class="num">${s.n} events · ${LABEL[s.v]}</small>${s.note ? `<p>${esc(s.note)}</p>` : ''}</div>`).join('') : '';

  pane.innerHTML = `<div class="pane-inner">
    <div class="head" data-v="${a.verdict}">
      <div><span class="pill">${LABEL[a.verdict]}</span></div>
      <h1>${esc(a.name)}</h1>
      <div class="sub"><span>${esc(a.industry)}</span><span>${a.locations} location${a.locations === 1 ? '' : 's'}</span><span>Local time: ${a.tz.replace('_', ' ')}</span>
        <span class="num">Checked ${local(recentStart, a.tz)} – ${local(recentEnd, a.tz)} vs the 8 weeks before</span></div>
    </div>
    <div class="callout" data-v="${a.verdict}"><strong>${LABEL[a.verdict]}</strong><span>${sentence}</span><ul>${reasonsHtml}</ul>
      <div class="actions"><button class="btn primary" type="button">Copy summary for ticket</button><button class="btn" type="button">Open in 1-day view</button></div></div>
    <section><h2>Signals <em>this week vs usual range</em></h2><div class="tablewrap"><table>
      <thead><tr><th>Signal</th><th class="r">This week</th><th>Usual range</th><th>Where it lands</th><th>Status</th></tr></thead><tbody>${rows}</tbody></table></div></section>
    ${sites ? `<section><h2>Locations <em>2 unusual · 3 worth a look · 10 normal</em></h2><div class="sites">${sites}</div></section>` : ''}
    <div class="two">
      <div class="card"><h3>When activity happened (local time)</h3><p>This week, by weekday and hour. Hatched columns are hours with under 2% of usual activity.</p>
        <div class="heat">${heat}</div><div class="hours">${hours}</div>
        <div class="legend"><span><i class="sw" style="background:var(--accent)"></i>more events</span><span><i class="sw rare" style="background:var(--heat-0);background-image:repeating-linear-gradient(135deg,transparent 0 3px,color-mix(in srgb,var(--muted) 30%,transparent) 3px 4px)"></i>unusual hour</span></div></div>
      <div class="card"><h3>${selected === 6 ? 'Lead conversion by week' : 'Events by week'}</h3><p>${selected === 6 ? 'Shaded box is the baseline period and its usual range (10%–59%).' : `Shaded box is the baseline period and its usual range (${bm.lo}–${bm.hi}).`}</p>${spark}</div>
    </div>
    <section><h2>Data quality</h2><div class="dq"><span><b class="num">${a.dups}</b> duplicate events removed</span><span><b class="num">${a.nullOutcomePct}%</b> of this week's events have no outcome</span><span>Rates exclude events with no outcome</span></div></section>
  </div>`;
})();
