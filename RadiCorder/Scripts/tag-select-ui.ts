export function clearMultiSelect(selectElm: HTMLSelectElement): void {
    Array.from(selectElm.options).forEach((option) => {
        option.selected = false;
    });
}

export function enableTouchLikeMultiSelect(selectElm: HTMLSelectElement): void {
    if (!selectElm.multiple) {
        return;
    }

    selectElm.addEventListener('mousedown', (event) => {
        if (event.button !== 0) {
            return;
        }

        if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) {
            return;
        }

        const target = event.target as EventTarget | null;
        if (!(target instanceof HTMLOptionElement)) {
            return;
        }

        event.preventDefault();
        target.selected = !target.selected;
        selectElm.dispatchEvent(new Event('change', { bubbles: true }));
    });
}

type RenderSelectedTagChipsOptions = {
    emptyClassName?: string;
    chipClassName?: string;
};

export function renderSelectedTagChips(
    selectElm: HTMLSelectElement | null,
    container: HTMLElement | null,
    options: RenderSelectedTagChipsOptions = {}): void {
    if (!selectElm || !container) {
        return;
    }

    container.innerHTML = '';
    const selectedOptions = Array.from(selectElm.selectedOptions);
    const emptyClassName = options.emptyClassName ?? 'rk-selected-tags-empty';
    const chipClassName = options.chipClassName ?? 'rk-selected-tag-chip';
    if (selectedOptions.length === 0) {
        const empty = document.createElement('span');
        empty.className = emptyClassName;
        empty.textContent = '未選択';
        container.appendChild(empty);
        return;
    }

    selectedOptions.forEach((option) => {
        const chip = document.createElement('span');
        chip.className = chipClassName;

        const text = document.createElement('span');
        text.textContent = option.textContent ?? option.value;

        const removeButton = document.createElement('button');
        removeButton.type = 'button';
        removeButton.setAttribute('aria-label', `${option.textContent ?? option.value} を解除`);
        removeButton.textContent = '×';
        removeButton.addEventListener('click', () => {
            option.selected = false;
            selectElm.dispatchEvent(new Event('change'));
        });

        chip.appendChild(text);
        chip.appendChild(removeButton);
        container.appendChild(chip);
    });
}
