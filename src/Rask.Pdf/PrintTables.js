// Cuts each table taller than `longerThan` pages into a table per printed page, where Chromium would break it anyway:
// one table across hundreds of pages costs Chromium's print layout memory for every row on every page (a 10,000-row
// table on 590 pages ran its renderer past 13 GB, and the print failed), a table per page costs it once.
// Evaluated by the protocol (the page's own scripts stay off) on the laid-out print media, at the page's width;
// `pageHeight` is a page's content height in the same CSS pixels. Returns how many tables it cut.
(pageHeight, longerThan) => {
  const slack = 1;
  const box = element => element.getBoundingClientRect();
  const pageRule = () => [...document.styleSheets].some(sheet => {
    try {
      return [...sheet.cssRules].some(rule => rule instanceof CSSPageRule);
    } catch {
      return true;
    }
  });
  if (pageRule()) {
    return 0;
  }

  // Every piece keeps the columns the whole table had: fixed, at the widths it was laid out with.
  const fixColumns = (table, rows) => {
    const widths = [];
    for (const row of [...(table.tHead?.rows ?? []), ...rows.slice(0, 50)]) {
      let column = 0;
      for (const cell of row.cells) {
        if (cell.colSpan === 1) {
          widths[column] ??= box(cell).width;
        }

        column += cell.colSpan;
      }
    }

    if (widths.length === 0 || widths.includes(undefined)) {
      return null;
    }

    const sample = rows.slice(0, 5);
    const before = sample.map(row => [...row.cells].map(cell => box(cell).width));
    const style = table.getAttribute('style');
    const columns = document.createElement('colgroup');
    for (const width of widths) {
      columns.appendChild(document.createElement('col')).style.width = `${width}px`;
    }

    table.style.width = `${box(table).width}px`;
    table.style.tableLayout = 'fixed';
    table.prepend(columns);
    if (sample.every((row, r) => [...row.cells].every((cell, c) => Math.abs(box(cell).width - before[r][c]) < 0.5))) {
      return columns;
    }

    columns.remove();
    style === null ? table.removeAttribute('style') : table.setAttribute('style', style);
    return null;
  };

  // A hidden row keeps a row's place in its section, so :first-child, :last-child and odd/even still pick the rows they did.
  const keeper = () => {
    const row = document.createElement('tr');
    row.style.setProperty('display', 'none', 'important');
    row.setAttribute('aria-hidden', 'true');
    return row;
  };

  // How much higher the next table sits than it will print: the room left at the foot of each page cut early.
  let shift = 0;

  const cut = table => {
    const rows = [...table.tBodies].flatMap(section => [...section.rows]);
    if (rows.length < 2
      || table.parentElement?.closest('table')
      || getComputedStyle(table).display !== 'table'
      || table.querySelector(':scope > colgroup, :scope > col, td[rowspan]:not([rowspan="1"]), th[rowspan]:not([rowspan="1"])')) {
      return false;
    }

    const columns = fixColumns(table, rows);
    if (!columns) {
      return false;
    }

    const whole = box(table);
    const first = box(rows[0]);
    const last = box(rows.at(-1));
    const head = first.top - whole.top - (table.caption ? box(table.caption).height : 0);
    // A row's height takes the spacing under it, so a piece's foot is only what is under the last row's spacing.
    const style = getComputedStyle(table);
    const spacing = style.borderCollapse === 'separate' ? parseFloat(style.borderSpacing.split(' ').at(-1)) : 0;
    const foot = whole.bottom - last.bottom - spacing;
    const tops = rows.map(row => box(row).top);
    const heights = tops.map((top, i) => (i + 1 < rows.length ? tops[i + 1] : last.bottom + spacing) - top);
    const room = pageHeight - head - foot - slack;
    if (heights.some(height => height > room)) {
      return false;
    }

    const cuts = [];
    let free = pageHeight - ((whole.top + scrollY + shift) % pageHeight) - (first.top - whole.top) - foot - slack;
    heights.forEach((height, i) => {
      if (height > free && i > 0) {
        cuts.push(i);
        shift += free + slack;
        free = room;
      }

      free -= height;
    });

    const place = rows.map(row => row.sectionRowIndex);
    const size = rows.map(row => row.parentElement.rows.length);
    const close = (section, i) => {
      if (place[i] < size[i] - 1) {
        section.append(keeper());
      }
    };

    if (cuts.length > 0) {
      close(rows[cuts[0] - 1].parentElement, cuts[0] - 1);
    }

    let previous = table;
    cuts.forEach((from, k) => {
      const piece = table.cloneNode(false);
      piece.removeAttribute('id');
      piece.style.breakBefore = 'page';
      piece.style.marginTop = '0';
      previous.style.marginBottom = '0';
      previous.after(piece);
      previous = piece;
      piece.append(columns.cloneNode(true));
      if (table.tHead) {
        piece.append(table.tHead.cloneNode(true));
      }

      const to = k + 1 < cuts.length ? cuts[k + 1] : rows.length;
      let section = null;
      let source = null;
      for (let i = from; i < to; i++) {
        if (source !== rows[i].parentElement) {
          if (section) {
            close(section, i - 1);
          }

          source = rows[i].parentElement;
          section = piece.appendChild(source.cloneNode(false));
          for (let n = place[i] === 0 ? 0 : 2 - (place[i] % 2); n > 0; n--) {
            section.append(keeper());
          }
        }

        section.append(rows[i]);
      }

      close(section, to - 1);
      if (table.tFoot) {
        piece.append(table.tFoot.cloneNode(true));
      }

    });

    return cuts.length > 0;
  };

  let count = 0;
  for (const table of [...document.querySelectorAll('table')]) {
    if (box(table).height > pageHeight * longerThan && cut(table)) {
      count++;
    }
  }

  return count;
}
