import { trapTab } from './trap-tab';

/** Tab kept inside an open popover or sheet (Phase 39.3). */
describe('trapTab', () => {
  let box: HTMLElement;

  beforeEach(() => {
    box = document.createElement('div');

    const heading = document.createElement('h2');
    const link = document.createElement('a');
    const off = document.createElement('button');
    const on = document.createElement('button');

    heading.tabIndex = -1;
    heading.textContent = 'Title';
    link.href = '/one';
    link.textContent = 'One';
    off.disabled = true;
    off.textContent = 'Off';
    on.textContent = 'Two';
    box.append(heading, link, off, on);
    document.body.appendChild(box);
  });

  afterEach(() => box.remove());

  const tab = (shiftKey = false) => {
    const event = new KeyboardEvent('keydown', { key: 'Tab', shiftKey, cancelable: true });

    trapTab(event, box);

    return event;
  };

  const one = () => box.querySelector<HTMLElement>('a')!;
  const two = () => box.querySelectorAll<HTMLElement>('button')[1];

  it('wraps from the last stop to the first, skipping what is disabled', () => {
    two().focus();

    expect(tab().defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(one());
  });

  it('wraps from the first stop, or from the heading, to the last with Shift+Tab', () => {
    one().focus();
    expect(tab(true).defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(two());

    box.querySelector<HTMLElement>('h2')!.focus();
    expect(tab(true).defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(two());
  });

  it('leaves every other Tab, and every other key, to the browser', () => {
    one().focus();

    expect(tab().defaultPrevented).toBe(false);

    const enter = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true });

    trapTab(enter, box);
    expect(enter.defaultPrevented).toBe(false);
  });
});
