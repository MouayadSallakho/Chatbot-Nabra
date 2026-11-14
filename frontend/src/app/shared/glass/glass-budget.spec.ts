import { GLASS_LAYER_BUDGET, countBlurredLayers } from './glass-budget';

describe('countBlurredLayers', () => {
  let stage: HTMLDivElement;

  beforeEach(() => {
    stage = document.createElement('div');
    document.body.appendChild(stage);
  });

  afterEach(() => stage.remove());

  /** jsdom has no layout: give each element a box on screen. */
  function layer(filter: string, box = { width: 100, height: 40, top: 10, left: 10 }): HTMLElement {
    const element = document.createElement('div');
    element.style.setProperty('backdrop-filter', filter);
    element.getBoundingClientRect = () =>
      ({ ...box, bottom: box.top + box.height, right: box.left + box.width }) as DOMRect;
    stage.appendChild(element);
    return element;
  }

  it('counts the elements that blur what is behind them', () => {
    layer('blur(24px) saturate(140%)');
    layer('blur(24px) saturate(140%)');
    layer('none');

    expect(countBlurredLayers(document)).toBe(2);
  });

  it('ignores hidden and off-screen layers', () => {
    layer('blur(24px)').style.display = 'none';
    layer('blur(24px)', { width: 100, height: 40, top: -500, left: 10 });

    expect(countBlurredLayers(document)).toBe(0);
  });

  it('allows three on screen at once', () => {
    expect(GLASS_LAYER_BUDGET).toBe(3);
  });
});
