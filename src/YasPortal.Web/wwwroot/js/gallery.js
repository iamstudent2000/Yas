// Gallery slider: the arrow buttons next to a row of pictures move the row sideways by most of its width.
// One listener on the whole page is enough (it keeps working when Blazor redraws the page), and the move is instant (no animation).
document.addEventListener('click', function (e) {
    var button = e.target.closest('[data-gallery-scroll]');
    if (!button) return;
    var row = button.closest('.gallery-slider').querySelector('.gallery-row');
    // The number is 1 or -1; the move is by screen direction, so it works the same in right-to-left pages.
    row.scrollBy({ left: Number(button.getAttribute('data-gallery-scroll')) * row.clientWidth * 0.8, behavior: 'auto' });
});
