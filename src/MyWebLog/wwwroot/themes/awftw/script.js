const awftw = {
  counted: false,
  countPlay: function (fileLink) {
    if (!this.counted) {
      const request = new XMLHttpRequest()
      request.open('HEAD', 'https://pdcst.click/c/awftw/files.bitbadger.solutions/devotions/' + fileLink, true)
      request.onload = function () { awftw.counted = true }
      request.onerror = function () { }
      request.send()
    }
  }
}
