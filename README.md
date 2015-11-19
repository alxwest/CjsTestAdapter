CasperJs Test Adapter
======================
CasperJs TestAdapter for Visual Studio unit test projects.

This adapter will allow CasperJs tests to be recognised by Visal Studio unit test projects. At the moment if will only recognise the test using the regex "casper\.test\.begin\('(.+?)',", not ideal but I need to find a decent JS syntax tree explorer, suggestions are welcome.

To use just build and install the VSIX. The adapter is system independent so there's no prerequisite to have PhantomJs or CasperJs installed.

### Current Versions

- CasperJs : 1.1.Beta 
- PhantomJs : 1.9.2 

### Roadmap
- VisualStudio extensions library release.
- SlimerJs integration.
- TrifleJs integration.
- Intellisense for CasperJs tests.

### Thanks
This adapter was based on Mathew Aniyan's post [Writing a Visual Studio 2012 Unit Test Adapter](http://blogs.msdn.com/b/visualstudioalm/archive/2012/07/31/writing-a-visual-studio-2012-unit-test-adapter.aspx)

