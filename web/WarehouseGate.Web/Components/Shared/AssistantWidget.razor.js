export function scrollToLatest(element) {
    element?.scrollIntoView({
        behavior: "smooth",
        block: "end"
    });
}
