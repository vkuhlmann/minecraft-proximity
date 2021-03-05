"use strict";

let currentTab = "home";

function setTab(name) {
    $(`#${currentTab}`)[0].classList.remove("active");
    $(`#${currentTab}`)[0].classList.remove("show");
    $(`#${currentTab}-tab`)[0].classList.remove("active");

    $(`#${name}`)[0].classList.add("active");
    $(`#${name}`)[0].classList.add("show");
    $(`#${name}-tab`)[0].classList.add("active");
    currentTab = name;
}
